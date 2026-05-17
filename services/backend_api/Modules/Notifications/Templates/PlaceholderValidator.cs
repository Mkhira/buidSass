using System.Text.RegularExpressions;

namespace BackendApi.Modules.Notifications.Templates;

/// <summary>
/// Extracts <c>{name}</c> placeholders from a rendered string and validates
/// that the runtime substitution payload supplies every placeholder. Used by
/// <see cref="TemplateRenderer"/> at render time and by the
/// <c>SubmitForReview</c> / <c>Approve</c> handlers at validation time so a
/// reviewer cannot publish a template that references a placeholder it has
/// not declared.
/// </summary>
public static class PlaceholderValidator
{
    /// <summary>
    /// Recognized placeholder shape: <c>{name}</c> where <c>name</c> is one or
    /// more alphanumeric or underscore characters. Matches once per
    /// occurrence (no nested / escaped variants — keep the surface tight so
    /// editorial AR copy with prose braces does not false-positive).
    /// </summary>
    private static readonly Regex PlaceholderPattern =
        new(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns the distinct placeholder names referenced in the input,
    /// preserving first-seen order.
    /// </summary>
    public static IReadOnlyList<string> Extract(string body)
    {
        if (string.IsNullOrEmpty(body)) return Array.Empty<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (Match m in PlaceholderPattern.Matches(body))
        {
            var name = m.Groups[1].Value;
            if (seen.Add(name)) result.Add(name);
        }
        return result;
    }

    /// <summary>
    /// Returns the set of placeholders that appear in <paramref name="body"/>
    /// but are NOT in the <paramref name="declared"/> list. An empty list
    /// means the template is internally consistent and safe to publish.
    /// </summary>
    public static IReadOnlyList<string> UndeclaredIn(string body, IEnumerable<string> declared)
    {
        var declaredSet = new HashSet<string>(declared, StringComparer.Ordinal);
        return Extract(body).Where(p => !declaredSet.Contains(p)).ToArray();
    }

    /// <summary>
    /// Validates a draft / in-review template version. Throws
    /// <see cref="InvalidOperationException"/> with a message that lists the
    /// undeclared placeholders if any are present.
    /// </summary>
    public static void EnsureNoUndeclaredPlaceholders(
        string bodyAr, string bodyEn, IEnumerable<string> declaredPlaceholders)
    {
        var declared = declaredPlaceholders as IList<string> ?? declaredPlaceholders.ToList();
        var undeclaredAr = UndeclaredIn(bodyAr, declared);
        var undeclaredEn = UndeclaredIn(bodyEn, declared);

        if (undeclaredAr.Count == 0 && undeclaredEn.Count == 0) return;

        var parts = new List<string>(2);
        if (undeclaredAr.Count > 0) parts.Add($"ar=[{string.Join(",", undeclaredAr)}]");
        if (undeclaredEn.Count > 0) parts.Add($"en=[{string.Join(",", undeclaredEn)}]");
        throw new InvalidOperationException(
            $"Template references undeclared placeholders: {string.Join("; ", parts)}.");
    }
}
