using BackendApi.Modules.AuditLog;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.B2B.Hooks;

/// <summary>
/// Spec 021 tasks T140–T142 / research §R13. Subscribes to
/// <see cref="ICustomerAccountLifecycleSubscriber"/> events from spec 004 (Identity)
/// and propagates them into the B2B module:
///
/// <list type="bullet">
///   <item><c>OnAccountLockedAsync</c> — voids every non-terminal quote owned by the
///         customer (state → <c>withdrawn</c>, reason <c>account_inactive</c>).</item>
///   <item><c>OnAccountDeletedAsync</c> — voids every non-terminal quote AND
///         removes the customer's <see cref="CompanyMembership"/> rows. Companies
///         are NOT auto-cascaded; spec 019 handles the orphan-company case.</item>
///   <item><c>OnMarketChangedAsync</c> — voids every non-terminal quote across
///         markets (reason <c>customer_market_changed</c>); cross-market state
///         is not preserved (FR-027).</item>
/// </list>
///
/// All paths are idempotent — re-delivery of the event is a no-op (the
/// non-terminal set is already empty after the first handle). Subscriber failures
/// MUST NOT roll back the originating identity event (FR-043 / Principle 25).
/// </summary>
public sealed class AccountLifecycleHandler(
    B2BDbContext db,
    IAuditEventPublisher auditPublisher,
    IPublisher domainPublisher,
    TimeProvider clock,
    ILogger<AccountLifecycleHandler> logger) : ICustomerAccountLifecycleSubscriber
{
    private static readonly Guid SystemActorId = new("00000000-0000-0000-0000-00000B2B0210");

    public Task OnAccountLockedAsync(CustomerAccountLocked evt, CancellationToken ct) =>
        VoidQuotesAsync(
            customerId: evt.CustomerId,
            reasonToken: "account_inactive",
            removeMemberships: false,
            ct);

    public async Task OnAccountDeletedAsync(CustomerAccountDeleted evt, CancellationToken ct)
    {
        await VoidQuotesAsync(
            customerId: evt.CustomerId,
            reasonToken: "account_deleted",
            removeMemberships: true,
            ct);
    }

    public Task OnMarketChangedAsync(CustomerMarketChanged evt, CancellationToken ct) =>
        VoidQuotesAsync(
            customerId: evt.CustomerId,
            reasonToken: "customer_market_changed",
            removeMemberships: false,
            ct);

    private async Task VoidQuotesAsync(
        Guid customerId,
        string reasonToken,
        bool removeMemberships,
        CancellationToken ct)
    {
        var nowUtc = clock.GetUtcNow();

        // Read a lightweight snapshot of candidate quotes (id + market + prior
        // state for ledger metadata). The actual transition is performed via a
        // per-row conditional UPDATE so a quote that advances between this read
        // and the write is left alone — `rowsAffected == 0` is the signal that
        // we lost the race, and we skip emitting a ledger row / event for that
        // quote rather than overwriting stale state.
        var snapshots = await db.Quotes
            .AsNoTracking()
            .Where(q => q.CustomerId == customerId
                     && (q.State == "requested"
                      || q.State == "drafted"
                      || q.State == "revised"
                      || q.State == "pending-approver"))
            .Select(q => new { q.Id, q.State, q.CompanyId, q.MarketCode })
            .ToListAsync(ct);

        var withdrawnToken = QuoteState.Withdrawn.ToToken();
        var voidedQuoteIds = new List<(Guid Id, string PriorState, Guid? CompanyId, string Market)>();
        foreach (var snap in snapshots)
        {
            // Atomic write-time guard: only transition rows whose DB-visible
            // state is still in the non-terminal set. A concurrent writer that
            // already moved this row out of that set causes 0 rows affected and
            // we treat it as a no-op for this hook.
            var rowsAffected = await db.Quotes
                .Where(q => q.Id == snap.Id
                         && (q.State == "requested"
                          || q.State == "drafted"
                          || q.State == "revised"
                          || q.State == "pending-approver"))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(q => q.State, withdrawnToken)
                    .SetProperty(q => q.TerminalAt, (DateTimeOffset?)nowUtc)
                    .SetProperty(q => q.TerminalReason, reasonToken), ct);

            if (rowsAffected == 0)
            {
                continue;
            }

            db.QuoteStateTransitions.Add(new QuoteStateTransition
            {
                Id = Guid.NewGuid(),
                QuoteId = snap.Id,
                MarketCode = snap.MarketCode,
                PriorState = snap.State,
                NewState = withdrawnToken,
                ActorKind = QuoteActorKind.System.ToToken(),
                ActorId = null,
                ReasonJson = null,
                MetadataJson = "{\"reason\":\"" + reasonToken + "\"}",
                OccurredAt = nowUtc,
            });

            voidedQuoteIds.Add((snap.Id, snap.State, snap.CompanyId, snap.MarketCode));
        }

        // Account-deleted only: remove company memberships so the deleted user no
        // longer authorizes against the company. Companies themselves are not
        // touched here — that orphan-company decision is owned by spec 019.
        var memberships = removeMemberships
            ? await db.CompanyMemberships.Where(m => m.UserId == customerId).ToListAsync(ct)
            : new List<CompanyMembership>();
        var removedMembershipSnapshots = memberships
            .Select(m => (m.Id, m.CompanyId, m.Role, m.MarketCode))
            .ToList();
        if (memberships.Count > 0)
        {
            db.CompanyMemberships.RemoveRange(memberships);
        }

        await db.SaveChangesAsync(ct);

        // Best-effort fan-out — a subscriber failure MUST NOT roll back the void.
        foreach (var (id, priorState, companyId, market) in voidedQuoteIds)
        {
            try
            {
                await auditPublisher.PublishAsync(new AuditEvent(
                    ActorId: SystemActorId,
                    ActorRole: "system",
                    Action: "quote.state_changed",
                    EntityType: "quote",
                    EntityId: id,
                    BeforeState: new { state = priorState },
                    AfterState: new { state = QuoteState.Withdrawn.ToToken(), reason = reasonToken },
                    Reason: reasonToken), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "AccountLifecycleHandler audit publish failed for quote {QuoteId}.", id);
            }

            try
            {
                await domainPublisher.Publish(new QuoteWithdrawn(
                    QuoteId: id,
                    CustomerId: customerId,
                    CompanyId: companyId,
                    Reason: reasonToken,
                    MarketCode: market), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "AccountLifecycleHandler domain publish failed for quote {QuoteId}.", id);
            }
        }

        // Audit each removed membership — Principle 25 requires a trail for role /
        // permission changes. Best-effort fan-out, same pattern as the quote leg.
        foreach (var (membershipId, companyId, role, marketCode) in removedMembershipSnapshots)
        {
            try
            {
                await auditPublisher.PublishAsync(new AuditEvent(
                    ActorId: SystemActorId,
                    ActorRole: "system",
                    Action: "company_membership.removed",
                    EntityType: "company_membership",
                    EntityId: membershipId,
                    BeforeState: new { company_id = companyId, role, market_code = marketCode },
                    AfterState: null,
                    Reason: reasonToken), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "AccountLifecycleHandler membership-removal audit publish failed for membership {MembershipId}.", membershipId);
            }
        }
    }
}
