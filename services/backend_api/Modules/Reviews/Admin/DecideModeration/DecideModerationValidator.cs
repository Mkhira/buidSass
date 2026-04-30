using BackendApi.Modules.Reviews.Primitives;

namespace BackendApi.Modules.Reviews.Admin.DecideModeration;

public static class DecideModerationValidator
{
    private static readonly IReadOnlySet<string> AllowedToStates = new HashSet<string>(StringComparer.Ordinal)
    {
        "visible",
        "hidden",
        "deleted",
    };

    public static (bool ok, string? reasonCode, string? detail) Validate(DecideModerationRequest? body)
    {
        if (body is null)
        {
            return (false, ReviewReasonCode.ModerationReasonRequired, "Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(body.ToState) || !AllowedToStates.Contains(body.ToState))
        {
            return (false, ReviewReasonCode.ModerationInvalidState,
                "to_state must be one of visible, hidden, deleted.");
        }

        // Hidden + deleted require reason_note ≥ 10 chars.
        if (body.ToState is "hidden" or "deleted")
        {
            if (string.IsNullOrWhiteSpace(body.ReasonNote) || body.ReasonNote.Trim().Length < 10)
            {
                return (false, ReviewReasonCode.ModerationReasonRequired,
                    "reason_note of at least 10 characters required for this transition.");
            }
        }

        // Visible (reinstate) requires admin_note ≥ 10 chars.
        if (body.ToState == "visible")
        {
            if (string.IsNullOrWhiteSpace(body.AdminNote) || body.AdminNote.Trim().Length < 10)
            {
                return (false, ReviewReasonCode.ModerationReasonRequired,
                    "admin_note of at least 10 characters required to reinstate a review.");
            }
        }

        return (true, null, null);
    }
}
