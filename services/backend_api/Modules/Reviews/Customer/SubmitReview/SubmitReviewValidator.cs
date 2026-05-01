using BackendApi.Modules.Reviews.Primitives;

namespace BackendApi.Modules.Reviews.Customer.SubmitReview;

/// <summary>
/// Field-level validator for the submit-review request. Surfaces stable
/// reason codes from <see cref="ReviewReasonCode"/> per contract §2.1.
/// </summary>
public static class SubmitReviewValidator
{
    public static (bool ok, string? reasonCode, string? detail) Validate(SubmitReviewRequest? body)
    {
        // Null request body. The spec's contract §10 doesn't define a generic
        // "body_required" code; we surface BodyLengthInvalid since an absent
        // body is the extreme case of "body not within 1-4000 chars". The
        // detail string disambiguates for the client.
        if (body is null) return (false, ReviewReasonCode.BodyLengthInvalid, "Request body is required.");

        // Empty / missing product_id. Same rationale: no dedicated code exists
        // in contract §10, so we surface BodyLengthInvalid + a specific detail.
        // If the spec adds review.product_id.required, swap here.
        if (body.ProductId == Guid.Empty) return (false, ReviewReasonCode.BodyLengthInvalid, "product_id is required and must be a non-empty GUID.");

        if (body.Rating < 1 || body.Rating > 5)
        {
            return (false, ReviewReasonCode.RatingOutOfRange, "Rating must be between 1 and 5.");
        }

        if (string.IsNullOrWhiteSpace(body.Headline) || body.Headline.Length > 100)
        {
            return (false, ReviewReasonCode.HeadlineLengthInvalid, "Headline must be 1-100 characters.");
        }

        if (string.IsNullOrWhiteSpace(body.Body) || body.Body.Length > 4000)
        {
            return (false, ReviewReasonCode.BodyLengthInvalid, "Body must be 1-4000 characters.");
        }

        if (!IsValidLocale(body.Locale))
        {
            return (false, ReviewReasonCode.LocaleInvalid, "Locale must be 'ar' or 'en'.");
        }

        if (body.MediaUrls is { Count: > 4 })
        {
            return (false, ReviewReasonCode.MediaTooMany, "At most 4 media URLs allowed per review.");
        }

        if (body.MediaUrls is not null)
        {
            foreach (var url in body.MediaUrls)
            {
                if (!IsValidSignedUrl(url))
                {
                    return (false, ReviewReasonCode.MediaInvalidSignedUrl, "Each media URL must be a non-empty https:// URL.");
                }
            }
        }

        return (true, null, null);
    }

    private static bool IsValidLocale(string? locale) =>
        locale is "ar" or "en";

    private static bool IsValidSignedUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            && parsed.Scheme is "https";
    }
}
