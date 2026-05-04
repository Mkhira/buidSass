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

namespace BackendApi.Modules.B2B.Quotes.Customer.RequestRevision;

/// <summary>
/// Spec 021 contract §2.6 — handler for
/// <c>POST /api/customer/quotes/{id}/request-revision</c>. Customer-initiated
/// non-terminal transition; quote moves <c>revised → drafted</c>
/// (operator-only-visible).
///
/// <para>
/// <strong>Permission</strong> (§2.6): caller must be the customer-owner OR hold
/// <c>buyer</c> membership for the quote's company. <c>approver</c> and
/// <c>companies.admin</c> memberships confer READ access (per §2.3) but NOT
/// revision-request authority — distinguished here from
/// <see cref="CustomerQuoteVisibility"/>'s read-side gate. The handler returns 404
/// (not 403) when the caller can READ but cannot REQUEST-REVISION, preserving
/// visibility-leak symmetry with §2.4 / §2.5.
/// </para>
///
/// <para>
/// <strong>State machine</strong>: <see cref="QuoteStateMachine.CanTransition"/>
/// permits <c>revised → drafted</c> for <c>actorKind=Customer | Buyer</c>.
/// Other source states (requested / drafted / pending-approver / terminal) emit
/// <c>409 quote.invalid_state_for_action</c>.
/// </para>
///
/// <para>
/// <strong>Comment preservation</strong>: §2.6 says "<c>customer_revision_comment</c>
/// is preserved on the next QuoteVersion". The next QuoteVersion is created by US3
/// (admin AuthorQuoteDraft / PublishQuoteVersion) — so this handler can't write to
/// it directly (no QuoteVersion exists yet at this point). We stash the comment
/// in the transition row's <see cref="QuoteStateTransition.MetadataJson"/> under
/// the key <c>customer_revision_comment</c>. US3's handler queries the most-recent
/// <c>revised → drafted</c> transition for the quote and copies the comment onto
/// the new <see cref="QuoteVersion.CustomerRevisionCommentJson"/> when it publishes.
/// </para>
///
/// <para>
/// <strong>Atomicity</strong>: same pattern as
/// <c>WithdrawQuoteHandler</c> — quote-state mutation + transition row commit
/// together; audit + domain event publish AFTER the commit, isolated by try/catch
/// (FR-043). xmin row-version guards optimistic concurrency.
/// </para>
///
/// <para>
/// <strong>Hard-delete</strong>: forbidden — request-revision is a state move,
/// never a row delete.
/// </para>
/// </summary>
public sealed class RequestRevisionHandler
{
    private readonly B2BDbContext _db;
    private readonly IAuditEventPublisher _auditPublisher;
    private readonly IPublisher _domainPublisher;
    private readonly TimeProvider _time;
    private readonly ILogger<RequestRevisionHandler> _logger;

    public RequestRevisionHandler(
        B2BDbContext db,
        IAuditEventPublisher auditPublisher,
        IPublisher domainPublisher,
        TimeProvider time,
        ILogger<RequestRevisionHandler> logger)
    {
        _db = db;
        _auditPublisher = auditPublisher;
        _domainPublisher = domainPublisher;
        _time = time;
        _logger = logger;
    }

    public async Task<RequestRevisionResult> HandleAsync(
        Guid quoteId,
        Guid customerId,
        RequestRevisionRequest body,
        CancellationToken ct)
    {
        // ---------- 1. Visibility gate ----------
        var visibleCompanyIds = await CustomerQuoteVisibility
            .GetVisibleCompanyIdsAsync(_db, customerId, ct);

        var visibleQuote = await CustomerQuoteVisibility.GetVisibleQuoteAsync(
            _db, quoteId, customerId, visibleCompanyIds, ct);

        if (visibleQuote is null)
        {
            return RequestRevisionResult.NotFound();
        }

        // ---------- 2. Authority gate (customer-owner OR buyer) ----------
        var canRequestRevision = await CallerCanRequestRevisionAsync(visibleQuote, customerId, ct);
        if (!canRequestRevision)
        {
            return RequestRevisionResult.NotFound();
        }

        // ---------- 3. Re-fetch with tracking ----------
        var tracked = await _db.Quotes
            .FirstOrDefaultAsync(q => q.Id == quoteId, ct);
        if (tracked is null)
        {
            // Race / hard-delete defense — should not happen in V1 (no DELETEs).
            return RequestRevisionResult.NotFound();
        }

        // ---------- 4. State-machine gate ----------
        if (!QuoteStateExtensions.TryParseToken(tracked.State, out var currentState))
        {
            _logger.LogError(
                "Quote {QuoteId} has unrecognized state token '{State}' — rejecting request-revision.",
                tracked.Id, tracked.State);
            return RequestRevisionResult.InvalidStateForAction();
        }

        var actorKind = tracked.CompanyId is null
            ? QuoteActorKind.Customer
            : QuoteActorKind.Buyer;

        if (!QuoteStateMachine.CanTransition(currentState, QuoteState.Drafted, actorKind))
        {
            // Only `revised` is a valid source for this transition with a customer/buyer
            // actor. Everything else (requested / drafted / pending-approver / terminal)
            // surfaces 409 invalid_state_for_action.
            return RequestRevisionResult.InvalidStateForAction();
        }

        // ---------- 5. Build comment payload ----------
        // Validator already enforced at-least-one-locale + length. Persist only
        // non-empty fields so storage stays compact.
        var commentJson = SerializeComment(body.Comment!);

        // ---------- 6. Mutate + persist ----------
        var nowUtc = _time.GetUtcNow();
        var priorStateToken = tracked.State;

        tracked.State = QuoteState.Drafted.ToToken();

        // The transition row stores the comment in metadata so US3 (admin authoring)
        // can read it when creating the next QuoteVersion. ReasonJson holds the
        // bilingual comment for audit-side display; MetadataJson signals which
        // locale the buyer wrote in (operator UI uses this for default highlighting).
        var transition = new QuoteStateTransition
        {
            Id = Guid.NewGuid(),
            QuoteId = tracked.Id,
            MarketCode = tracked.MarketCode,
            PriorState = priorStateToken,
            NewState = QuoteState.Drafted.ToToken(),
            ActorKind = actorKind.ToToken(),
            ActorId = customerId,
            ReasonJson = commentJson,
            MetadataJson = JsonSerializer.Serialize(new
            {
                kind = "customer_revision_request",
                comment_locale_hint = ResolveLocaleHint(body.Comment!),
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
            return RequestRevisionResult.InvalidStateForAction();
        }

        // ---------- 7. Audit + domain event (post-commit, isolated) ----------
        try
        {
            await _auditPublisher.PublishAsync(new AuditEvent(
                ActorId: customerId,
                ActorRole: actorKind == QuoteActorKind.Customer ? "customer" : "buyer",
                Action: "quote.state_changed",
                EntityType: "quote",
                EntityId: tracked.Id,
                BeforeState: new { state = priorStateToken },
                AfterState: new { state = tracked.State },
                Reason: "customer_revision_request"), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Audit publish failed for quote.state_changed request-revision "
                + "(quote_id={QuoteId}, customer_id={CustomerId}). Quote already committed.",
                tracked.Id, customerId);
        }

        try
        {
            await _domainPublisher.Publish(new QuoteRevisionRequested(
                QuoteId: tracked.Id,
                CustomerId: tracked.CustomerId,
                CompanyId: tracked.CompanyId,
                ActorUserId: customerId,
                MarketCode: tracked.MarketCode,
                CommentLocaleHint: ResolveLocaleHint(body.Comment!)), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "QuoteRevisionRequested domain-event publish failed (quote_id={QuoteId}). "
                + "Operator queue (spec 025) will not fire for this revision request unless replayed.",
                tracked.Id);
        }

        return RequestRevisionResult.Success(new RequestRevisionResponse(
            Id: tracked.Id,
            State: tracked.State));
    }

    /// <summary>
    /// §2.6 write-authority check. Returns true when the caller can REQUEST-REVISION:
    /// individual quote owners (no company_id) OR <c>buyer</c> members of the quote's
    /// company. <c>approver</c> and <c>companies.admin</c> roles can READ but cannot
    /// request-revision — handler surfaces 404 for that case.
    /// </summary>
    private async Task<bool> CallerCanRequestRevisionAsync(Quote quote, Guid customerId, CancellationToken ct)
    {
        if (quote.CompanyId is null)
        {
            return quote.CustomerId == customerId;
        }

        return await _db.CompanyMemberships
            .AsNoTracking()
            .AnyAsync(m => m.CompanyId == quote.CompanyId.Value
                        && m.UserId == customerId
                        && m.Role == "buyer", ct);
    }

    private static string SerializeComment(LocalizedComment comment)
    {
        var hasEn = !string.IsNullOrWhiteSpace(comment.En);
        var hasAr = !string.IsNullOrWhiteSpace(comment.Ar);
        // Validator guarantees at least one is non-empty; defensive default.
        return JsonSerializer.Serialize(new
        {
            en = hasEn ? comment.En : null,
            ar = hasAr ? comment.Ar : null,
        });
    }

    private static string ResolveLocaleHint(LocalizedComment comment)
    {
        // Prefer Arabic when only Arabic is supplied; otherwise default English.
        if (!string.IsNullOrWhiteSpace(comment.Ar) && string.IsNullOrWhiteSpace(comment.En))
        {
            return "ar";
        }
        return "en";
    }
}

/// <summary>
/// Internal handler envelope. The endpoint translates this into <see cref="Results.Ok"/>,
/// 404, or 409 with the appropriate <see cref="QuoteReasonCode"/>.
/// </summary>
public sealed record RequestRevisionResult(
    bool IsSuccess,
    int StatusCode,
    QuoteReasonCode? ReasonCode,
    RequestRevisionResponse? Response)
{
    public static RequestRevisionResult Success(RequestRevisionResponse r) =>
        new(true, 200, null, r);

    public static RequestRevisionResult NotFound() =>
        new(false, 404, QuoteReasonCode.QuoteNotFound, null);

    public static RequestRevisionResult InvalidStateForAction() =>
        new(false, 409, QuoteReasonCode.QuoteInvalidStateForAction, null);
}
