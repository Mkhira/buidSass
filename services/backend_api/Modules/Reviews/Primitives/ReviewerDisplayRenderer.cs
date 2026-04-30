namespace BackendApi.Modules.Reviews.Primitives;

/// <summary>
/// Canonical reviewer-display renderer per FR-016a / Clarification Q5. The same
/// rule is used by storefront and the moderator queue (single source of truth);
/// computed at read time (never denormalized) so name changes in spec 019
/// propagate without a backfill.
/// </summary>
public static class ReviewerDisplayRenderer
{
    /// <summary>
    /// Renders <paramref name="firstName"/> + space + last-initial + dot, OR
    /// returns the customer-chosen <paramref name="displayHandle"/> when set.
    /// Empty / whitespace inputs collapse safely.
    /// </summary>
    public static string Render(string? displayHandle, string? firstName, string? lastName)
    {
        if (!string.IsNullOrWhiteSpace(displayHandle))
        {
            return displayHandle.Trim();
        }

        var first = (firstName ?? string.Empty).Trim();
        var last = (lastName ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(last))
        {
            return string.IsNullOrEmpty(first) ? "—" : first;
        }

        var initial = char.ToUpper(EnumerateFirstRune(last));
        return string.IsNullOrEmpty(first)
            ? $"{initial}."
            : $"{first} {initial}.";
    }

    private static char EnumerateFirstRune(string s)
    {
        // Simple first-char read; stable for both AR and EN (renderer is initial-only).
        // Composed surrogate pairs are rare for AR/EN names; fallback to '?' is safe.
        return s.Length > 0 ? s[0] : '?';
    }
}
