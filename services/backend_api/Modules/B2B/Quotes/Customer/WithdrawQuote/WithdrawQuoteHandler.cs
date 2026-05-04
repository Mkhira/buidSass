using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.Quotes.Customer.Visibility;
using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.B2B.Quotes.Customer.WithdrawQuote;

/// <summary>
/// Spec 021 contract §2.5 — handler for <c>POST /api/customer/quotes/{id}/withdraw</c>.
/// Customer-initiated terminal transition; quote moves to <c>withdrawn</c> from any
/// non-terminal state.
///
/// <para>
/// <strong>Permission</strong> (§2.5): caller must be the customer-owner OR hold
/// <c>companies.admin</c> membership for the quote's company. <c>buyer</c> and
/// <c>approver</c> memberships confer READ access (per §2.3) but NOT withdraw
/// authority — distinguished here from <see cref="CustomerQuoteVisibility"/>'s
/// read-side gate. The handler returns 404 (not 403) when the caller can READ but
/// cannot WITHDRAW, preserving visibility-leak symmetry.
/// </para>
///
/// <para>
/// <strong>State machine</strong>: <see cref="QuoteStateMachine.CanTransition"/> with
/// <c>actorKind=Customer</c> for individual quotes or <c>actorKind=Buyer</c> for
/// company quotes (the state machine treats both as legitimate withdraw initiators
/// from any non-terminal state). Terminal-state attempts emit
/// <c>409 quote.invalid_state_for_action</c>.
/// </para>
///
/// <para>
/// <strong>Atomicity</strong>: Quote-state mutation, <see cref="QuoteStateTransition"/>
/// row, and <see cref="QuoteWithdrawn"/> domain event commit together via a single
/// <see cref="DbContext.SaveChangesAsync"/>. The MediatR domain-event publish + the
/// audit-event publish run AFTER the commit succeeds — a thrown subscriber MUST NOT
/// 500 the response (FR-043 / Cycle B fix). Optimistic concurrency relies on the
/// <c>xmin</c> row-version: if another writer mutates the quote between our read and
/// our save, EF surfaces <see cref="DbUpdateConcurrencyException"/>; we catch and
/// emit <c>quote.invalid_state_for_action</c> (a concurrent writer most likely
/// transitioned the quote to a terminal state).
/// </para>
///
/// <para>
/// <strong>Idempotency</strong>: idempotency-key replay storage lands with
/// SubmitAcceptance (Cycle C-3 / T070). For Cycle C-2, the endpoint enforces header
/// presence; the handler's race-protection comes from xmin + state-machine gate.
/// A duplicate withdraw attempt on an already-withdrawn quote naturally surfaces
/// <c>quote.invalid_state_for_action</c>.
/// </para>
///
/// <para>
/// <strong>Hard-delete</strong>: forbidden — withdrawn quotes remain in the
/// <c>quotes</c> table with <c>state='withdrawn'</c> + <c>terminal_at</c> populated;
/// the immutable transition ledger preserves the timeline.
/// </para>
/// </summary>
public sealed class WithdrawQuoteHandler
{
    private readonly B2BDbContext _db;
    private readonly IAuditEventPublisher _auditPublisher;
    private readonly IPublisher _domainPublisher;
    private readonly TimeProvider _time;
    private readonly ILogger<WithdrawQuoteHandler> _logger;

    public WithdrawQuoteHandler(
        B2BDbContext db,
        IAuditEventPublisher auditPublisher,
        IPublisher domainPublisher,
        TimeProvider time,
        ILogger<WithdrawQuoteHandler> logger)
    {
        _db = db;
        _auditPublisher = auditPublisher;
        _domainPublisher = domainPublisher;
        _time = time;
        _logger = logger;
    }

    public async Task<WithdrawQuoteResult> HandleAsync(
        Guid quoteId,
        Guid customerId,
        WithdrawQuoteRequest body,
        CancellationToken ct)
    {
        // ---------- 1. Visibility gate ----------
        var visibleCompanyIds = await CustomerQuoteVisibility
            .GetVisibleCompanyIdsAsync(_db, customerId, ct);

        var visibleQuote = await CustomerQuoteVisibility.GetVisibleQuoteAsync(
            _db, quoteId, customerId, visibleCompanyIds, ct);

        if (visibleQuote is null)
        {
            // Not found OR not visible — collapse both branches to 404 per §2.4
            // visibility-leak prevention (§2.5 inherits the same convention).
            return WithdrawQuoteResult.NotFound();
        }

        // ---------- 2. Authority gate (customer-owner OR companies.admin) ----------
        // Read-visibility (buyer/approver) is necessary but not sufficient: §2.5
        // restricts WRITE to customer-owner + companies.admin only. Surface 404
        // for read-but-not-write to preserve visibility symmetry across endpoints.
        var initialAuthority = await ResolveAuthorityAsync(visibleQuote, customerId, ct);
        if (initialAuthority == WithdrawAuthority.None)
        {
            return WithdrawQuoteResult.NotFound();
        }

        // ---------- 3. Re-fetch with tracking ----------
        // Visibility helper returned an AsNoTracking() snapshot. To mutate state
        // we need a tracked entity; re-fetch with default tracking enabled. The
        // xmin row-version on the tracked entity guards against a concurrent
        // writer that may have advanced the state between the visibility read
        // and this fetch.
        var tracked = await _db.Quotes
            .FirstOrDefaultAsync(q => q.Id == quoteId, ct);
        if (tracked is null)
        {
            // Race: quote was deleted between the visibility check and this read.
            // Hard-delete is forbidden, so this should not happen — defensive.
            return WithdrawQuoteResult.NotFound();
        }

        // ---------- 3a. Re-validate authority on tracked entity (TOCTOU guard) ----------
        // Memberships can be revoked between the visibility check and this fetch;
        // §2.5 must enforce permissions as of the WRITE moment, not the read.
        // Re-run the authority predicate against the tracked row inside the same
        // unit of work (CodeRabbit Round 1 — Critical).
        var authority = await ResolveAuthorityAsync(tracked, customerId, ct);
        if (authority == WithdrawAuthority.None)
        {
            return WithdrawQuoteResult.NotFound();
        }

        // ---------- 4. State-machine gate ----------
        if (!QuoteStateExtensions.TryParseToken(tracked.State, out var currentState))
        {
            // DB CHECK constraint enforces the enum; an unrecognized token here
            // means the constraint was bypassed (drift / migration error). Treat
            // as invalid-state-for-action; logger captures for ops.
            _logger.LogError(
                "Quote {QuoteId} has unrecognized state token '{State}' — rejecting withdraw.",
                tracked.Id, tracked.State);
            return WithdrawQuoteResult.InvalidStateForAction();
        }

        // Map the resolved write-authority onto the persisted actor_kind. The DB
        // CHECK constraint and state-machine vocabulary do not currently include
        // a "companies.admin" actor token; we keep the closest aligned token
        // (Buyer for company-quote write paths) so the state machine still
        // accepts the transition, and we record the precise authority on the
        // transition's MetadataJson + the audit envelope so the trail is
        // accurate (CodeRabbit Round 1 — Major).
        var actorKind = tracked.CompanyId is null
            ? QuoteActorKind.Customer
            : QuoteActorKind.Buyer;
        var actorRoleToken = authority switch
        {
            WithdrawAuthority.IndividualOwner => "customer",
            WithdrawAuthority.CompanyAdmin => "companies.admin",
            _ => throw new InvalidOperationException("Unreachable: authority validated above."),
        };

        if (!QuoteStateMachine.CanTransition(currentState, QuoteState.Withdrawn, actorKind))
        {
            // Terminal-state attempts (already withdrawn / accepted / rejected /
            // expired) land here. Self-transition (currentState == Withdrawn)
            // also denied by the state machine, surfacing the same code so a
            // duplicate withdraw call deterministically returns 409.
            return WithdrawQuoteResult.InvalidStateForAction();
        }

        // ---------- 5. Mutate + persist ----------
        var nowUtc = _time.GetUtcNow();
        var priorStateToken = tracked.State;
        var reasonToken = string.IsNullOrWhiteSpace(body.Reason)
            ? "customer_initiated"
            : body.Reason.Trim();

        tracked.State = QuoteState.Withdrawn.ToToken();
        tracked.TerminalAt = nowUtc;
        tracked.TerminalReason = reasonToken;

        var transition = new QuoteStateTransition
        {
            Id = Guid.NewGuid(),
            QuoteId = tracked.Id,
            MarketCode = tracked.MarketCode,
            PriorState = priorStateToken,
            NewState = QuoteState.Withdrawn.ToToken(),
            ActorKind = actorKind.ToToken(),
            ActorId = customerId,
            ReasonJson = null,
            MetadataJson = JsonSerializer.Serialize(new
            {
                reason_token = reasonToken,
                actor_role = actorRoleToken,
            }),
            OccurredAt = nowUtc,
        };
        _db.QuoteStateTransitions.Add(transition);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // xmin guard tripped — another writer mutated the quote since our
            // tracked-fetch. Most likely they transitioned to a terminal state;
            // surface invalid-state-for-action so the client retries by reading
            // the current state.
            return WithdrawQuoteResult.InvalidStateForAction();
        }

        // ---------- 6. Audit + domain event (post-commit, isolated) ----------
        // Same pattern as Cycle B's RequestQuoteFromCart: state writes never block
        // on subscriber delivery (FR-043). A throwing publisher logs at Error;
        // OperationCanceledException still bubbles to honor caller cancellation.
        //
        // KNOWN TECH-DEBT: this puts the audit publish OUTSIDE the SaveChanges
        // transaction. If the audit publisher throws, the quote is committed but
        // no audit row exists — Principle 25 expects every state-changing action
        // to be auditable. The proper fix is an outbox pattern (write a domain-events
        // row in the same transaction; a publisher worker drains it). Outbox infra
        // is a Phase 1.5 cross-spec concern; the LogError below ensures any failed
        // publish is caught by ops monitoring for manual replay until the outbox
        // lands. This trade-off is a deliberate cycle-B-vintage choice mirrored here
        // for consistency across the customer state-mutation surface.
        try
        {
            await _auditPublisher.PublishAsync(new AuditEvent(
                ActorId: customerId,
                ActorRole: actorRoleToken,
                Action: "quote.state_changed",
                EntityType: "quote",
                EntityId: tracked.Id,
                BeforeState: new { state = priorStateToken },
                AfterState: new { state = tracked.State, terminal_at = tracked.TerminalAt, terminal_reason = reasonToken },
                Reason: reasonToken), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Audit publish failed for quote.state_changed withdraw (quote_id={QuoteId}, "
                + "customer_id={CustomerId}). Quote already committed; audit-pipeline retry "
                + "path is responsible for replay.",
                tracked.Id, customerId);
        }

        try
        {
            await _domainPublisher.Publish(new QuoteWithdrawn(
                QuoteId: tracked.Id,
                CustomerId: tracked.CustomerId,
                CompanyId: tracked.CompanyId,
                Reason: reasonToken,
                MarketCode: tracked.MarketCode), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "QuoteWithdrawn domain-event publish failed (quote_id={QuoteId}). "
                + "Notification subscribers (spec 025) will not fire for this withdraw "
                + "unless replayed.",
                tracked.Id);
        }

        return WithdrawQuoteResult.Success(new WithdrawQuoteResponse(
            Id: tracked.Id,
            State: tracked.State,
            TerminalAt: tracked.TerminalAt!.Value));
    }

    /// <summary>
    /// §2.5 write-authority check. Returns the resolved authority granting the
    /// caller permission to WITHDRAW the quote (or <see cref="WithdrawAuthority.None"/>
    /// when neither path applies). Returning the resolved authority — instead of
    /// a bare bool — lets the handler stamp accurate actor_role metadata on the
    /// transition + audit envelope (CodeRabbit Round 1 — Major).
    ///
    /// <para><c>buyer</c> and <c>approver</c> roles can READ but cannot withdraw —
    /// handler surfaces 404 for that case so the wire surface doesn't leak
    /// "you can see this but you can't withdraw it".</para>
    /// </summary>
    private async Task<WithdrawAuthority> ResolveAuthorityAsync(Quote quote, Guid customerId, CancellationToken ct)
    {
        if (quote.CompanyId is null)
        {
            // Individual customer quote — only the owner can withdraw.
            return quote.CustomerId == customerId
                ? WithdrawAuthority.IndividualOwner
                : WithdrawAuthority.None;
        }

        // Company quote — only companies.admin role.
        var hasAdminRole = await _db.CompanyMemberships
            .AsNoTracking()
            .AnyAsync(m => m.CompanyId == quote.CompanyId.Value
                        && m.UserId == customerId
                        && m.Role == "companies.admin", ct);
        return hasAdminRole ? WithdrawAuthority.CompanyAdmin : WithdrawAuthority.None;
    }
}

/// <summary>
/// Resolved write-authority for §2.5 withdraw. Carried from the authority gate
/// into the audit/transition payloads so the action is attributed to the exact
/// role that authorized it.
/// </summary>
internal enum WithdrawAuthority
{
    None = 0,
    IndividualOwner = 1,
    CompanyAdmin = 2,
}

/// <summary>
/// Internal handler envelope. The endpoint translates this into <see cref="Results.Ok"/>,
/// 404, or 409 with the appropriate <see cref="QuoteReasonCode"/>.
/// </summary>
public sealed record WithdrawQuoteResult(
    bool IsSuccess,
    int StatusCode,
    QuoteReasonCode? ReasonCode,
    WithdrawQuoteResponse? Response)
{
    public static WithdrawQuoteResult Success(WithdrawQuoteResponse r) =>
        new(true, 200, null, r);

    public static WithdrawQuoteResult NotFound() =>
        new(false, 404, QuoteReasonCode.QuoteNotFound, null);

    public static WithdrawQuoteResult InvalidStateForAction() =>
        new(false, 409, QuoteReasonCode.QuoteInvalidStateForAction, null);
}
