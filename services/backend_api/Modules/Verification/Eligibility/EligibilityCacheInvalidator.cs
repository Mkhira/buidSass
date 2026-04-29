using System.Text.Json;
using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackendApi.Modules.Verification.Eligibility;

/// <summary>
/// Rebuilds the <c>verification_eligibility_cache</c> row for a given
/// <c>(customer_id, market_code)</c> tuple inside the same Tx that performs a
/// state transition (data-model §2.6 / FR-024). No I/O outside the passed
/// <see cref="VerificationDbContext"/> — caller must commit via
/// <c>SaveChangesAsync</c>.
/// </summary>
/// <remarks>
/// Algorithm (per data-model §2.6 / §4):
/// <list type="number">
///   <item>Find the customer's most-recent active <see cref="VerificationState.Approved"/>
///         verification <b>in this market</b> (highest <c>SubmittedAt</c>; ignores
///         <c>Superseded</c> / <c>Expired</c> / <c>Revoked</c> / <c>Void</c>).</item>
///   <item>If found: write/UPDATE the <c>(customer_id, market_code)</c> cache
///         row with <see cref="EligibilityClass.Eligible"/> wire form
///         ("eligible"), <c>ExpiresAt</c> mirrored, <c>Professions</c> =
///         <c>[approval.profession]</c>.</item>
///   <item>If none found: write/UPDATE with class "ineligible" and no professions.</item>
/// </list>
/// US3 (Phase 5) extends this with the <c>IProductRestrictionPolicy</c> join for
/// SKU-specific eligibility decisions; this V1 implementation handles the
/// customer/market-level coarse class which is what every transition needs to
/// refresh. Per ADR-010 markets are independently regulated, so EG and KSA
/// eligibility rows are completely separate — rebuilding one MUST NOT touch
/// the other.
/// </remarks>
public sealed class EligibilityCacheInvalidator
{
    public async Task RebuildAsync(
        Guid customerId,
        string marketCode,
        VerificationDbContext db,
        CancellationToken ct)
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
        //
        // Market-scoped: only rows in the same market participate in this
        // particular cache row's eligibility decision. ADR-010 partitioning.
        var pendingInMarket = db.ChangeTracker.Entries<Entities.Verification>()
            .Where(e => e.State is EntityState.Modified or EntityState.Added)
            .Select(e => e.Entity)
            .Where(v => v.CustomerId == customerId && v.MarketCode == marketCode)
            .ToList();

        var pendingIds = pendingInMarket.Select(v => v.Id).ToHashSet();

        var pendingApprovedInMemory = pendingInMarket
            .Where(v => v.State == VerificationState.Approved)
            .OrderByDescending(v => v.SubmittedAt)
            .FirstOrDefault();

        var committedApproved = await db.Verifications
            .AsNoTracking()
            .Where(v => v.CustomerId == customerId
                     && v.MarketCode == marketCode
                     && v.State == VerificationState.Approved
                     && !pendingIds.Contains(v.Id))
            .OrderByDescending(v => v.SubmittedAt)
            .FirstOrDefaultAsync(ct);

        // In-memory pending Approved wins (it's the most-recent decision the caller is
        // about to commit). Falls back to a committed approval that has no pending
        // mutation. Both being null means the customer has no active approval in this
        // market.
        var activeApproval = pendingApprovedInMemory ?? committedApproved;

        var nowUtc = DateTimeOffset.UtcNow;

        string eligibilityClass;
        DateTimeOffset? expiresAt;
        string? reasonCode;
        string professionsJson;

        if (activeApproval is null)
        {
            eligibilityClass = "ineligible";
            expiresAt = null;
            reasonCode = EligibilityReasonCode.VerificationRequired.ToWireValue();
            professionsJson = "[]";
        }
        else
        {
            eligibilityClass = "eligible";
            expiresAt = activeApproval.ExpiresAt;
            reasonCode = null;
            professionsJson = JsonSerializer.Serialize(new[] { activeApproval.Profession });
        }

        // Real UPSERT against the (CustomerId, MarketCode) composite PK. Two
        // concurrent transitions rebuilding the same cache row no longer race
        // each other (read-then-insert/update could blow up on PK conflict and
        // roll back the whole verification transition). ON CONFLICT DO UPDATE
        // is atomic at the storage layer and runs in the ambient EF
        // transaction, so the caller's SaveChanges still commits everything
        // together.
        //
        // Also detach any change-tracked cache row to avoid double-writes — we
        // own all writes to this row from this method.
        var trackedExisting = db.ChangeTracker.Entries<VerificationEligibilityCache>()
            .FirstOrDefault(e => e.Entity.CustomerId == customerId
                              && e.Entity.MarketCode == marketCode);
        if (trackedExisting is not null)
        {
            trackedExisting.State = EntityState.Detached;
        }

        const string upsertSql = """
            INSERT INTO verification.verification_eligibility_cache
                ("CustomerId", "MarketCode", "EligibilityClass", "ExpiresAt", "ReasonCode", "Professions", "ComputedAt")
            VALUES (@p_customer, @p_market, @p_class, @p_expires, @p_reason, @p_professions::jsonb, @p_now)
            ON CONFLICT ("CustomerId", "MarketCode") DO UPDATE SET
                "EligibilityClass" = EXCLUDED."EligibilityClass",
                "ExpiresAt"        = EXCLUDED."ExpiresAt",
                "ReasonCode"       = EXCLUDED."ReasonCode",
                "Professions"      = EXCLUDED."Professions",
                "ComputedAt"       = EXCLUDED."ComputedAt";
            """;

        await db.Database.ExecuteSqlRawAsync(
            upsertSql,
            parameters: new object[]
            {
                new NpgsqlParameter("p_customer", customerId),
                new NpgsqlParameter("p_market", marketCode),
                new NpgsqlParameter("p_class", eligibilityClass),
                new NpgsqlParameter("p_expires", (object?)expiresAt ?? DBNull.Value),
                new NpgsqlParameter("p_reason", (object?)reasonCode ?? DBNull.Value),
                new NpgsqlParameter("p_professions", professionsJson),
                new NpgsqlParameter("p_now", nowUtc),
            },
            cancellationToken: ct);
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
