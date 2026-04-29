using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Verification.Eligibility;
using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Verification.Hooks;

/// <summary>
/// Spec 020 tasks T110–T111 / research §R6 + §R7 / FR-027 + FR-038.
/// Subscribes to <see cref="ICustomerAccountLifecycleSubscriber"/> events
/// from spec 004 (Identity) and propagates them into the verification module:
///
/// <list type="bullet">
///   <item><c>OnAccountLockedAsync</c> — voids any non-terminal verification
///         + supersedes any active approval. Reason: <c>account_inactive</c>.</item>
///   <item><c>OnAccountDeletedAsync</c> — same as locked PLUS expedites
///         document purge by setting <c>purge_after = now</c> on every
///         non-purged document the customer owns (privacy hard-stop).</item>
///   <item><c>OnMarketChangedAsync</c> — voids non-terminal verifications and
///         supersedes any active approval IN THE FROM-MARKET ONLY.
///         Cross-market verification is not conferred (FR-027); the customer
///         must submit fresh in the new market. Reason:
///         <c>customer_market_changed</c>.</item>
/// </list>
///
/// All paths are idempotent — re-delivery of the event is a no-op (the
/// non-terminal set is already empty after the first handle).
/// </summary>
public sealed class AccountLifecycleHandler(
    VerificationDbContext db,
    EligibilityCacheInvalidator eligibilityInvalidator,
    IAuditEventPublisher auditPublisher,
    IVerificationDomainEventPublisher domainPublisher,
    TimeProvider clock,
    ILogger<AccountLifecycleHandler> logger) : ICustomerAccountLifecycleSubscriber
{
    public async Task OnAccountLockedAsync(CustomerAccountLocked evt, CancellationToken ct)
    {
        await VoidAndSupersedeAsync(
            customerId: evt.CustomerId,
            marketScope: null, // all markets
            voidReason: "account_inactive",
            supersedeReason: "account_inactive",
            expediteDocPurge: false,
            ct);
    }

    public async Task OnAccountDeletedAsync(CustomerAccountDeleted evt, CancellationToken ct)
    {
        await VoidAndSupersedeAsync(
            customerId: evt.CustomerId,
            marketScope: null,
            voidReason: "account_deleted",
            supersedeReason: "account_deleted",
            expediteDocPurge: true,
            ct);
    }

    public async Task OnMarketChangedAsync(CustomerMarketChanged evt, CancellationToken ct)
    {
        await VoidAndSupersedeAsync(
            customerId: evt.CustomerId,
            marketScope: evt.FromMarket,
            voidReason: "customer_market_changed",
            supersedeReason: "customer_market_changed",
            expediteDocPurge: false,
            ct);
    }

    private async Task VoidAndSupersedeAsync(
        Guid customerId,
        string? marketScope,
        string voidReason,
        string supersedeReason,
        bool expediteDocPurge,
        CancellationToken ct)
    {
        var nowUtc = clock.GetUtcNow();

        // Snapshot the impacted rows in one read so the rest of the work runs
        // against a stable set. Filter by market when scoped (FR-027).
        var rows = await db.Verifications
            .Where(v => v.CustomerId == customerId
                     && (marketScope == null || v.MarketCode == marketScope))
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return;
        }

        // Build marketsTouched from the snapshot (not from in-flight transitions
        // only) so a redelivery whose first attempt persisted state but failed
        // mid-rebuild can still heal the cache for those markets — terminal
        // rows on a redelivery would otherwise skip cache rebuild permanently
        // (CR R2 finding).
        var marketsTouched = rows
            .Select(r => r.MarketCode)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            // Only act on non-terminal rows + active approvals for state
            // transitions; terminal rows still need purge acceleration on
            // delete (CR R2 finding) so we DON'T `continue` past the doc
            // block for them — only the state-transition block is skipped.
            var priorState = row.State;
            switch (priorState)
            {
                case VerificationState.Submitted:
                case VerificationState.InReview:
                case VerificationState.InfoRequested:
                    row.State = VerificationState.Void;
                    row.VoidReason = voidReason;
                    row.UpdatedAt = nowUtc;
                    db.StateTransitions.Add(BuildTransition(row.Id, row.MarketCode, priorState, VerificationState.Void, voidReason, nowUtc));
                    break;

                case VerificationState.Approved:
                    row.State = VerificationState.Superseded;
                    row.UpdatedAt = nowUtc;
                    db.StateTransitions.Add(BuildTransition(row.Id, row.MarketCode, priorState, VerificationState.Superseded, supersedeReason, nowUtc));
                    break;

                    // default: already terminal — fall through to the document
                    // block. On account-delete, terminal rows still need their
                    // documents reaped immediately (privacy hard-stop).
            }

            // Stamp purge_after on documents. For account-deleted (privacy
            // hard-stop) we expedite to "now"; otherwise normal retention.
            // Terminal rows that fell through the switch still execute this
            // block — important on the delete path.
            if (expediteDocPurge)
            {
                var docs = await db.Documents
                    .Where(d => d.VerificationId == row.Id && d.PurgedAt == null)
                    .ToListAsync(ct);
                foreach (var doc in docs)
                {
                    doc.PurgeAfter = nowUtc;
                }
            }
            else
            {
                var schema = await db.MarketSchemas
                    .AsNoTracking()
                    .Where(s => s.MarketCode == row.MarketCode && s.Version == row.SchemaVersion)
                    .SingleOrDefaultAsync(ct);
                var retentionMonths = schema?.RetentionMonths ?? 24;
                var purgeAfter = nowUtc.AddMonths(retentionMonths);
                var docs = await db.Documents
                    .Where(d => d.VerificationId == row.Id && d.PurgedAt == null && d.PurgeAfter == null)
                    .ToListAsync(ct);
                foreach (var doc in docs)
                {
                    doc.PurgeAfter = purgeAfter;
                }
            }
        }

        // Persist the entity changes BEFORE rebuilding the eligibility cache —
        // the invalidator's UPSERT runs as ExecuteSqlRawAsync which auto-
        // commits; reversing the order means a SaveChangesAsync failure leaves
        // the cache out of sync with the verification rows. CR R1 finding.
        await db.SaveChangesAsync(ct);

        foreach (var market in marketsTouched)
        {
            await eligibilityInvalidator.RebuildAsync(customerId, market, db, ct);
        }

        // Audit per touched row — best-effort; lifecycle propagation MUST NOT
        // roll back on subscriber failure (FR-034). Pick the reason that
        // matches the row's terminal state (Superseded vs Void) so the audit
        // record is honest about which path the row took (CR R1 finding).
        foreach (var row in rows.Where(r => r.UpdatedAt == nowUtc))
        {
            var rowReason = row.State == VerificationState.Superseded ? supersedeReason : voidReason;
            try
            {
                await auditPublisher.PublishAsync(new AuditEvent(
                    ActorId: VerificationSystemActor.Id,
                    ActorRole: "system",
                    Action: "verification.state_changed",
                    EntityType: "verification",
                    EntityId: row.Id,
                    BeforeState: null,
                    AfterState: new { state = row.State.ToWireValue(), reason = rowReason },
                    Reason: rowReason), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "AccountLifecycleHandler audit publish failed for verification {VerificationId}.",
                    row.Id);
            }
        }

        // Domain events for spec 025 — best-effort. Voided non-terminals emit
        // VerificationVoided; superseded approvals emit VerificationSuperseded.
        foreach (var row in rows.Where(r => r.UpdatedAt == nowUtc))
        {
            try
            {
                // Lifecycle-driven supersession (account locked/deleted/market-changed)
                // has no "new verification id" — emit VerificationVoided for both
                // Void and Superseded paths so spec 025 can render a single
                // "verification is no longer valid" notification.
                if (row.State == VerificationState.Void || row.State == VerificationState.Superseded)
                {
                    await domainPublisher.PublishAsync(new VerificationDomainEvents.VerificationVoided(
                        VerificationId: row.Id,
                        CustomerId: row.CustomerId,
                        Reason: row.State == VerificationState.Superseded ? supersedeReason : voidReason), ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "AccountLifecycleHandler domain publish failed for verification {VerificationId}.",
                    row.Id);
            }
        }
    }

    private static VerificationStateTransition BuildTransition(
        Guid verificationId,
        string marketCode,
        VerificationState priorState,
        VerificationState newState,
        string reason,
        DateTimeOffset nowUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            VerificationId = verificationId,
            MarketCode = marketCode,
            PriorState = priorState.ToWireValue(),
            NewState = newState.ToWireValue(),
            ActorKind = VerificationActorKind.System.ToWireValue(),
            ActorId = null,
            Reason = reason,
            MetadataJson = "{}",
            OccurredAt = nowUtc,
        };
}
