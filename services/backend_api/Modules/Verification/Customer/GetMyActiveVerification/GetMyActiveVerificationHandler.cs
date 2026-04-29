using System.Text.Json;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Verification.Customer.GetMyActiveVerification;

/// <summary>
/// Spec 020 contracts §2.2 / tasks T055. Returns the customer's current
/// verification row + computed <c>renewal_open</c> + <c>next_action</c>
/// derived from the row's state and the snapshotted schema.
/// Selection order: any non-terminal row → otherwise the most-recent
/// approved row → otherwise null.
/// </summary>
public sealed class GetMyActiveVerificationHandler(
    VerificationDbContext db,
    TimeProvider clock)
{
    public async Task<GetMyActiveVerificationResponse?> HandleAsync(
        Guid customerId,
        CancellationToken ct)
    {
        var nowUtc = clock.GetUtcNow();

        // 1. Most-recent non-terminal row wins (the customer is in flight).
        var nonTerminal = await db.Verifications
            .AsNoTracking()
            .Where(v => v.CustomerId == customerId
                     && (v.State == VerificationState.Submitted
                      || v.State == VerificationState.InReview
                      || v.State == VerificationState.InfoRequested))
            .OrderByDescending(v => v.SubmittedAt)
            .FirstOrDefaultAsync(ct);

        // 2. Otherwise: most-recent approved row.
        var picked = nonTerminal
            ?? await db.Verifications
                .AsNoTracking()
                .Where(v => v.CustomerId == customerId && v.State == VerificationState.Approved)
                .OrderByDescending(v => v.SubmittedAt)
                .FirstOrDefaultAsync(ct);

        if (picked is null)
        {
            return null;
        }

        var (renewalOpen, nextAction) = await DeriveRenewalAndNextActionAsync(picked, nowUtc, ct);

        return new GetMyActiveVerificationResponse(
            Id: picked.Id,
            State: picked.State.ToWireValue(),
            MarketCode: picked.MarketCode,
            Profession: picked.Profession,
            SubmittedAt: picked.SubmittedAt,
            DecidedAt: picked.DecidedAt,
            ExpiresAt: picked.ExpiresAt,
            RenewalOpen: renewalOpen,
            NextAction: nextAction);
    }

    private async Task<(bool RenewalOpen, string NextAction)> DeriveRenewalAndNextActionAsync(
        Entities.Verification verification,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        switch (verification.State)
        {
            case VerificationState.Submitted:
            case VerificationState.InReview:
                return (false, "wait_for_review");
            case VerificationState.InfoRequested:
                return (false, "provide_info");
            case VerificationState.Approved:
                {
                    if (verification.ExpiresAt is null)
                    {
                        return (false, "verified");
                    }

                    var schema = await db.MarketSchemas
                        .AsNoTracking()
                        .Where(s => s.MarketCode == verification.MarketCode
                                 && s.Version == verification.SchemaVersion)
                        .SingleOrDefaultAsync(ct);

                    if (schema is null)
                    {
                        return (false, "verified");
                    }

                    var earliestReminderDays = ParseEarliestReminderWindowDays(schema.ReminderWindowsDaysJson);
                    var renewalOpensAt = verification.ExpiresAt.Value.AddDays(-earliestReminderDays);
                    var renewalOpen = nowUtc >= renewalOpensAt;
                    return (renewalOpen, renewalOpen ? "renew" : "verified");
                }
            default:
                return (false, "none");
        }
    }

    private static int ParseEarliestReminderWindowDays(string reminderWindowsJson)
    {
        if (string.IsNullOrWhiteSpace(reminderWindowsJson))
        {
            return 30;
        }
        try
        {
            var arr = JsonSerializer.Deserialize<int[]>(reminderWindowsJson);
            if (arr is null || arr.Length == 0)
            {
                return 30;
            }
            // Schema stores descending (e.g., [30, 14, 7, 1]); the earliest
            // window is the largest value.
            var max = 0;
            foreach (var d in arr)
            {
                if (d > max) max = d;
            }
            return max == 0 ? 30 : max;
        }
        catch (JsonException)
        {
            return 30;
        }
    }
}
