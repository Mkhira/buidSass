using System.Text;
using System.Text.Json;

namespace BackendApi.Modules.Identity.Primitives;

public sealed class IdentityRateLimitPartitionMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var propertyName = ResolvePropertyName(path);
        if (propertyName is not null)
        {
            var raw = await TryReadJsonPropertyAsync(context, propertyName);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                context.Items[RateLimitPolicies.IdentifierItemKey] = NormalizeIdentifier(raw);
            }
        }

        await _next(context);
    }

    private static string? ResolvePropertyName(string path)
    {
        if (path.EndsWith("/v1/customer/identity/sign-in", StringComparison.OrdinalIgnoreCase))
        {
            return "identifier";
        }

        if (path.EndsWith("/v1/admin/identity/sign-in", StringComparison.OrdinalIgnoreCase))
        {
            return "email";
        }

        if (path.EndsWith("/v1/customer/identity/otp/request", StringComparison.OrdinalIgnoreCase))
        {
            return "phone";
        }

        if (path.EndsWith("/v1/customer/identity/password/reset-request", StringComparison.OrdinalIgnoreCase))
        {
            return "email";
        }

        return null;
    }

    private static async Task<string?> TryReadJsonPropertyAsync(HttpContext context, string propertyName)
    {
        if (context.Request.ContentLength is null or <= 0)
        {
            return null;
        }

        if (!context.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) ?? true)
        {
            return null;
        }

        context.Request.EnableBuffering();
        context.Request.Body.Position = 0;
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!document.RootElement.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return value.GetString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NormalizeIdentifier(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Contains('@'))
        {
            return trimmed.ToLowerInvariant();
        }

        var builder = new StringBuilder(trimmed.Length + 1);
        foreach (var ch in trimmed)
        {
            if (char.IsDigit(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.Length == 0 ? trimmed.ToLowerInvariant() : $"+{builder}";
    }
}
