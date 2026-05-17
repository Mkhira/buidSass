using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BackendApi.Modules.Notifications.Domain;
using BackendApi.Modules.Notifications.Primitives;

namespace BackendApi.Modules.Notifications.Templates;

/// <summary>
/// Handlebars-style placeholder renderer with locale + RTL preservation per
/// <c>research.md §3</c>:
/// <list type="bullet">
///   <item>For <c>locale='ar'</c> the email body is wrapped in
///     <c>&lt;html lang="ar" dir="rtl"&gt;</c> and inline alignment hints use
///     logical <c>start</c>/<c>end</c> rather than <c>left</c>/<c>right</c>
///     (catches mail clients that strip wrapper attributes).</item>
///   <item>Placeholders use the <c>{name}</c> grammar consumed by
///     <see cref="PlaceholderValidator"/>; missing values throw rather than
///     emitting a half-rendered body.</item>
///   <item>Channel-aware: SMS bodies are NOT HTML-wrapped (carrier-bound
///     length budget); email bodies get the document wrapper; push bodies
///     are plain-text fragments consumed by the platform notification UI.</item>
/// </list>
/// </summary>
public static class TemplateRenderer
{
    private static readonly Regex PlaceholderPattern =
        new(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Renders the locale-correct body of a published <see cref="TemplateVersion"/>
    /// with the supplied <paramref name="values"/> substituted in.
    /// </summary>
    /// <param name="version">
    /// The published or archived snapshot; the caller MUST resolve the snapshot
    /// (BR-8) before calling — the renderer never reaches into the DB.
    /// </param>
    /// <param name="channel">
    /// One of <see cref="NotificationsConstants.Channels"/>. Drives whether to
    /// wrap the body in an HTML document (email only).
    /// </param>
    /// <param name="locale">
    /// One of <see cref="NotificationsConstants.Locales"/>. Drives body
    /// selection and RTL wrapping for email.
    /// </param>
    /// <param name="values">
    /// Placeholder values keyed by placeholder name. Missing keys raise
    /// <see cref="InvalidOperationException"/> — defense-in-depth on top of
    /// <see cref="PlaceholderValidator"/>'s publish-time gate.
    /// </param>
    public static RenderResult Render(
        TemplateVersion version,
        string channel,
        string locale,
        IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(values);
        if (!NotificationsConstants.Locales.All.Contains(locale))
            throw new ArgumentOutOfRangeException(nameof(locale), locale, "Locale must be 'ar' or 'en'.");
        if (!NotificationsConstants.Channels.All.Contains(channel))
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown channel.");

        // Resolve locale-specific body + subject.
        var (rawBody, rawSubject) = locale == NotificationsConstants.Locales.Ar
            ? (version.BodyAr, version.SubjectAr)
            : (version.BodyEn, version.SubjectEn);

        if (string.IsNullOrEmpty(rawBody))
            throw new InvalidOperationException(
                $"TemplateVersion {version.Id} is missing the {locale} body.");

        // Substitute placeholders. Missing keys throw with a precise message.
        var substitutedBody = Substitute(rawBody, values);
        var substitutedSubject = !string.IsNullOrEmpty(rawSubject)
            ? Substitute(rawSubject, values)
            : null;

        // Channel-specific post-processing.
        var finalBody = channel switch
        {
            NotificationsConstants.Channels.Email => WrapHtmlForEmail(substitutedBody, locale),
            NotificationsConstants.Channels.Sms => substitutedBody,
            NotificationsConstants.Channels.Push => substitutedBody,
            _ => substitutedBody,
        };

        return new RenderResult(
            Subject: substitutedSubject,
            Body: finalBody,
            Locale: locale,
            IsRtl: locale == NotificationsConstants.Locales.Ar);
    }

    private static string Substitute(string template, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(template)) return template;
        var output = new StringBuilder(template.Length + 32);
        var lastIndex = 0;
        foreach (Match m in PlaceholderPattern.Matches(template))
        {
            output.Append(template, lastIndex, m.Index - lastIndex);
            var name = m.Groups[1].Value;
            if (!values.TryGetValue(name, out var value))
            {
                throw new InvalidOperationException(
                    $"Template requires placeholder '{name}' but no value was supplied.");
            }
            output.Append(value);
            lastIndex = m.Index + m.Length;
        }
        if (lastIndex < template.Length)
        {
            output.Append(template, lastIndex, template.Length - lastIndex);
        }
        return output.ToString();
    }

    /// <summary>
    /// Wraps a plain or partial-HTML body in an <c>&lt;html&gt;</c> document
    /// carrying the correct <c>lang</c> + <c>dir</c> attributes plus a
    /// default inline-style block that uses logical <c>start</c>/<c>end</c>
    /// rather than <c>left</c>/<c>right</c>. The wrapper is idempotent — a
    /// body already containing <c>&lt;html&gt;</c> is returned unchanged.
    /// </summary>
    private static string WrapHtmlForEmail(string body, string locale)
    {
        // Pre-rendered HTML documents pass through unchanged so reviewers can
        // hand-craft the full document for premium campaigns when needed.
        if (body.Contains("<html", StringComparison.OrdinalIgnoreCase))
        {
            return body;
        }

        var dir = locale == NotificationsConstants.Locales.Ar ? "rtl" : "ltr";
        var defaultAlign = locale == NotificationsConstants.Locales.Ar ? "right" : "left";

        return string.Create(CultureInfo.InvariantCulture, $$"""
            <!doctype html>
            <html lang="{{locale}}" dir="{{dir}}">
              <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width,initial-scale=1" />
                <style>
                  body { text-align: start; direction: {{dir}}; font-family: -apple-system, system-ui, sans-serif; }
                  /* fallback for clients that strip the dir attribute */
                  .rtl-fallback { text-align: {{defaultAlign}}; }
                </style>
              </head>
              <body>
                <div class="rtl-fallback">
            {{body}}
                </div>
              </body>
            </html>
            """);
    }
}

/// <summary>
/// Outcome of a single render. <see cref="Subject"/> is null for SMS/push.
/// </summary>
public sealed record RenderResult(string? Subject, string Body, string Locale, bool IsRtl);
