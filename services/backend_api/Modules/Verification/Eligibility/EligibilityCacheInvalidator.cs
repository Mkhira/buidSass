using System.Text.Json;
using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Verification.Eligibility;

/// <summary>
/// Rebuilds the <c>verification_eligibility_cache</c> row for a customer inside
/// the same Tx that performs a state transition (data-model §2.6 / FR-024).
/// No I/O outside the passed <see cref="VerificationDbContext"/> — caller must
/// commit via <c>SaveChangesAsync</c>.
/// </summary>
/// <remarks>
/// Algorithm (per data-model §2.6 / §4):
/// <list type="number">
///   <item>Find the customer's most-recent active <see cref="VerificationState.Approved"/>
///         verification (highest <c>SubmittedAt</c>; ignores <c>Superseded</c> /
///         <c>Expired</c> / <c>Revoked</c> / <c>Void</c>).</item>
///   <item>If found: write/UPDATE the cache row with
///         <see cref="EligibilityClass.Eligible"/> wire form ("eligible"),
///         <c>ExpiresAt</c> mirrored, <c>Professions</c> = <c>[approval.profession]</c>.</item>
///   <item>If none found: write/UPDATE with class "ineligible" and no professions.</item>
/// </list>
/// US3 (Phase 5) extends this with the <c>IProductRestrictionPolicy</c> join for
/// SKU-specific eligibility decisions; this V1 implementation handles the customer-
/// level coarse class which is what every transition needs to refresh.
/// </remarks>
public sealed class EligibilityCacheInvalidator
{
    public async Task RebuildAsync(Guid customerId, VerificationDbContext db, CancellationToken ct)
    {
        // Read both committed rows AND pending change-tracked rows so a transition
        // that's about to be SaveChanges'd in the same Tx (e.g., DecideApprove sets
        // State = Approved in memory, then calls this invalidator BEFORE SaveChanges)
        // is visible to the cache rebuild.
        //
        // Critical: also EXCLUDE change-tracked rows from the committed query —
        // otherwise a DecideRevoke that pre-saves sees the still-committed Approved
        // row (its pending mutation to Revoked hasn't been written yet) and the
        // cache stays incorrectly "eligible".
        var pendingByCustomer = db.ChangeTracker.Entries<Entities.Verification>()
            .Where(e => e.State is EntityState.Modified or EntityState.Added)
            .Select(e => e.Entity)
            .Where(v => v.CustomerId == customerId)
            .ToList();

        var pendingIds = pendingByCustomer.Select(v => v.Id).ToHashSet();

        var pendingApprovedInMemory = pendingByCustomer
            .Where(v => v.State == VerificationState.Approved)
            .OrderByDescending(v => v.SubmittedAt)
            .FirstOrDefault();

        var committedApproved = await db.Verifications
            .AsNoTracking()
            .Where(v => v.CustomerId == customerId
                     && v.State == VerificationState.Approved
                     && !pendingIds.Contains(v.Id))
            .OrderByDescending(v => v.SubmittedAt)
            .FirstOrDefaultAsync(ct);

        // In-memory pending Approved wins (it's the most-recent decision the caller is
        // about to commit). Falls back to a committed approval that has no pending
        // mutation. Both being null means the customer has no active approval.
        var activeApproval = pendingApprovedInMemory ?? committedApproved;

        var nowUtc = DateTimeOffset.UtcNow;

        var existing = await db.EligibilityCache
            .SingleOrDefaultAsync(c => c.CustomerId == customerId, ct);

        if (activeApproval is null)
        {
            // No active approval — record as ineligible. Professions is empty.
            if (existing is null)
            {
                db.EligibilityCache.Add(new VerificationEligibilityCache
                {
                    CustomerId = customerId,
                    MarketCode = "ksa", // best-effort default; refined when an approval exists
                    EligibilityClass = "ineligible",
                    ExpiresAt = null,
                    ReasonCode = EligibilityReasonCode.VerificationRequired.ToWireValue(),
                    ProfessionsJson = "[]",
                    ComputedAt = nowUtc,
                });
            }
            else
            {
                existing.EligibilityClass = "ineligible";
                existing.ExpiresAt = null;
                existing.ReasonCode = EligibilityReasonCode.VerificationRequired.ToWireValue();
                existing.ProfessionsJson = "[]";
                existing.ComputedAt = nowUtc;
            }
            return;
        }

        var professionsJson = JsonSerializer.Serialize(new[] { activeApproval.Profession });

        if (existing is null)
        {
            db.EligibilityCache.Add(new VerificationEligibilityCache
            {
                CustomerId = customerId,
                MarketCode = activeApproval.MarketCode,
                EligibilityClass = "eligible",
                ExpiresAt = activeApproval.ExpiresAt,
                ReasonCode = null,
                ProfessionsJson = professionsJson,
                ComputedAt = nowUtc,
            });
        }
        else
        {
            existing.MarketCode = activeApproval.MarketCode;
            existing.EligibilityClass = "eligible";
            existing.ExpiresAt = activeApproval.ExpiresAt;
            existing.ReasonCode = null;
            existing.ProfessionsJson = professionsJson;
            existing.ComputedAt = nowUtc;
        }
    }
}

internal static class EligibilityReasonCodeWireMapper
{
    public static string ToWireValue(this EligibilityReasonCode code) => code switch
    {
        EligibilityReasonCode.Eligible => "Eligible",
        EligibilityReasonCode.Unrestricted => "Unrestricted",
        EligibilityReasonCode.VerificationRequired => "VerificationRequired",
        EligibilityReasonCode.VerificationPending => "VerificationPending",
        EligibilityReasonCode.VerificationInfoRequested => "VerificationInfoRequested",
        EligibilityReasonCode.VerificationRejected => "VerificationRejected",
        EligibilityReasonCode.VerificationExpired => "VerificationExpired",
        EligibilityReasonCode.VerificationRevoked => "VerificationRevoked",
        EligibilityReasonCode.ProfessionMismatch => "ProfessionMismatch",
        EligibilityReasonCode.MarketMismatch => "MarketMismatch",
        EligibilityReasonCode.AccountInactive => "AccountInactive",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };
}
