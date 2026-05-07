using System.Text;

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

        var initial = RenderFirstInitial(last);
        return string.IsNullOrEmpty(first)
            ? $"{initial}."
            : $"{first} {initial}.";
    }

    /// <summary>
    /// Returns the upper-cased first scalar (Rune) of <paramref name="s"/> as a
    /// string. Uses <see cref="Rune.DecodeFromUtf16"/> instead of <c>s[0]</c>
    /// (m1) so a leading surrogate pair (non-BMP scalar — emoji, supplementary
    /// AR characters, etc.) is decoded correctly rather than producing a lone
    /// high-surrogate. Falls back to "?" on decode failure.
    /// </summary>
    private static string RenderFirstInitial(string s)
    {
        if (s.Length == 0) return "?";
        if (Rune.DecodeFromUtf16(s, out var rune, out _) != System.Buffers.OperationStatus.Done)
        {
            return "?";
        }
        // Rune.ToUpperInvariant returns a Rune whose ToString preserves the surrogate
        // pair when the scalar is non-BMP — covers AR-supplementary cases safely.
        return Rune.ToUpperInvariant(rune).ToString();
    }
}
