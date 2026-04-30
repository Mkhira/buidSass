using System.Text.Json;

namespace BackendApi.Modules.Reviews.Filtering;

/// <summary>
/// Pure function: returns <see langword="true"/> when at least one media URL
/// is attached to a review per FR-014a. The presence of any attachment forces
/// the review into <c>pending_moderation</c> regardless of profanity-filter
/// outcome (Clarification Q2).
/// </summary>
public static class MediaAttachmentDetector
{
    public static bool HasMedia(IEnumerable<string>? mediaUrls)
    {
        if (mediaUrls is null) return false;
        foreach (var url in mediaUrls)
        {
            if (!string.IsNullOrWhiteSpace(url)) return true;
        }
        return false;
    }

    /// <summary>Convenience overload reading the jsonb-array string stored on the entity.</summary>
    public static bool HasMedia(string? mediaUrlsJson)
    {
        if (string.IsNullOrWhiteSpace(mediaUrlsJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(mediaUrlsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return false;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return true;
            }
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
