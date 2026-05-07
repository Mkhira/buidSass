using System.Text.Json;
using System.Text.Json.Serialization;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.B2B.Conversion;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.B2B.Quotes.Approver.FinalizeAcceptance;

/// <summary>
/// Spec 021 contract §3.2 — approver finalizes the buyer's acceptance.
/// Per Clarifications Q1: first-action-wins. xmin guard prevents two approvers
/// from both winning. State <c>pending-approver → accepted</c>; on success the
/// converter is invoked; loser sees <c>409 quote.already_decided</c>.
/// </summary>
public sealed class FinalizeAcceptanceHandler
{
    private readonly B2BDbContext _db;
    private readonly QuoteToOrderConverter _converter;
    private readonly IAuditEventPublisher _auditPublisher;
    private readonly IPublisher _domainPublisher;
    private readonly TimeProvider _time;
    private readonly ILogger<FinalizeAcceptanceHandler> _logger;

    public FinalizeAcceptanceHandler(
        B2BDbContext db,
        QuoteToOrderConverter converter,
        IAuditEventPublisher auditPublisher,
        IPublisher domainPublisher,
        TimeProvider time,
        ILogger<FinalizeAcceptanceHandler> logger)
    {
        _db = db;
        _converter = converter;
        _auditPublisher = auditPublisher;
        _domainPublisher = domainPublisher;
        _time = time;
        _logger = logger;
    }

    public async Task<FinalizeResult> HandleAsync(
        Guid quoteId,
        Guid approverId,
        Guid idempotencyKey,
        bool taxPreviewDriftAcknowledged,
        CancellationToken ct,
        string callerLocaleHint = "en")
    {
        var quote = await _db.Quotes.FirstOrDefaultAsync(q => q.Id == quoteId, ct);
        if (quote is null) return FinalizeResult.NotFound();
        if (quote.CompanyId is null) return FinalizeResult.NotFound();

        var hasApproverRole = await _db.CompanyMemberships
            .AsNoTracking()
            .AnyAsync(m => m.CompanyId == quote.CompanyId && m.UserId == approverId && m.Role == "approver", ct);
        if (!hasApproverRole) return FinalizeResult.NotFound();

        if (!QuoteStateExtensions.TryParseToken(quote.State, out var current)) return FinalizeResult.InvalidState();
        if (current == QuoteState.Accepted || current == QuoteState.Rejected
            || current == QuoteState.Expired || current == QuoteState.Withdrawn)
        {
            // Race: another approver already finalized OR the quote already settled.
            return FinalizeResult.AlreadyDecided();
        }
        if (current != QuoteState.PendingApprover) return FinalizeResult.InvalidState();

        var nowUtc = _time.GetUtcNow();
        if (quote.ExpiresAt.HasValue && quote.ExpiresAt.Value <= nowUtc)
        {
            return FinalizeResult.Expired();
        }

        var conversion = await _converter.ConvertAsync(quote, approverId, idempotencyKey,
            taxPreviewDriftAcknowledged, ct);
        if (!conversion.IsSuccess)
        {
            if (conversion.ReasonCode == QuoteReasonCode.QuoteEligibilityRequired)
                return FinalizeResult.EligibilityRequired();
            if (conversion.ReasonCode == QuoteReasonCode.QuoteTaxPreviewDriftThresholdExceeded)
                return FinalizeResult.TaxDrift();
            // CodeRabbit Round 1: a converter failure with a null ReasonCode (spec 011
            // exception, missing version row, etc.) carries a free-text Detail. Log it
            // and surface 500 quote.invalid_state_for_action so the caller doesn't see
            // a misleading state-machine reject. The detail is logged at converter level.
            _logger.LogError(
                "FinalizeAcceptance: conversion failed without a known reason code (quote_id={QuoteId}, detail={Detail}).",
                quote.Id, conversion.Detail);
            return FinalizeResult.InvalidState();
        }

        // Re-validate state + expiry inside the finalize transaction (after the
        // converter call has potentially taken non-trivial wall-clock time).
        // Without this re-check, the QuoteExpiryWorker could mark the quote
        // `expired` between the initial expiry check above and the SaveChanges
        // commit below, resulting in a converted order against an
        // already-expired quote. The xmin token on `quote` is the EF concurrency
        // gate; the explicit re-reads here turn races into deterministic
        // 409 Expired / InvalidState responses.
        //
        // CodeRabbit Loop 1 (Major): capture commitNowUtc AFTER ReloadAsync so a
        // slow reload cannot cause us to compare ExpiresAt against a stale
        // timestamp captured before the reload (would let an expired quote slip
        // through). Use this fresh post-reload instant for both the expiry check
        // and the terminal/decision timestamps below.
        await _db.Entry(quote).ReloadAsync(ct);
        var commitNowUtc = _time.GetUtcNow();
        if (!QuoteStateExtensions.TryParseToken(quote.State, out var rechecked)
            || rechecked != QuoteState.PendingApprover)
        {
            if (rechecked == QuoteState.Accepted || rechecked == QuoteState.Rejected
                || rechecked == QuoteState.Expired || rechecked == QuoteState.Withdrawn)
            {
                return FinalizeResult.AlreadyDecided();
            }
            return FinalizeResult.InvalidState();
        }
        if (quote.ExpiresAt.HasValue && quote.ExpiresAt.Value <= commitNowUtc)
        {
            return FinalizeResult.Expired();
        }

        var priorState = quote.State;
        quote.State = QuoteState.Accepted.ToToken();
        quote.DecidedAt = commitNowUtc;
        quote.DecidedBy = approverId;
        quote.TerminalAt = commitNowUtc;
        quote.TerminalReason = "accepted";

        var transition = new QuoteStateTransition
        {
            Id = Guid.NewGuid(),
            QuoteId = quote.Id,
            MarketCode = quote.MarketCode,
            PriorState = priorState,
            NewState = quote.State,
            ActorKind = QuoteActorKind.Approver.ToToken(),
            ActorId = approverId,
            ReasonJson = null,
            MetadataJson = JsonSerializer.Serialize(new
            {
                idempotency_key = idempotencyKey,
                order_id = conversion.OrderId,
                was_idempotent_replay = conversion.WasIdempotentReplay,
            }),
            OccurredAt = commitNowUtc,
        };
        _db.QuoteStateTransitions.Add(transition);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // SC-009: concurrent finalize race lost — surface already_decided.
            return FinalizeResult.AlreadyDecided();
        }

        try
        {
            await _auditPublisher.PublishAsync(new AuditEvent(
                ActorId: approverId,
                ActorRole: "approver",
                Action: "quote.state_changed",
                EntityType: "quote",
                EntityId: quote.Id,
                BeforeState: new { state = priorState },
                AfterState: new { state = quote.State, order_id = conversion.OrderId },
                Reason: "approver_finalized"), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // CodeRabbit Round 2: log audit failures (Principle 25). Quote acceptance
            // is a critical action; silent swallow leaves a compliance gap.
            _logger.LogWarning(ex,
                "FinalizeAcceptance: failed to publish quote.state_changed audit "
                + "(quote_id={QuoteId}, order_id={OrderId}). Audit-pipeline replay required.",
                quote.Id, conversion.OrderId);
        }

        try
        {
            // CodeRabbit Round 2: when IsSuccess is true, OrderId is guaranteed by
            // ConversionResult.Success() — assert via .Value rather than masking
            // a real bug with Guid.Empty.
            await _domainPublisher.Publish(new QuoteAccepted(
                QuoteId: quote.Id,
                OrderId: conversion.OrderId!.Value,
                CustomerId: quote.CustomerId,
                CompanyId: quote.CompanyId,
                MarketCode: quote.MarketCode,
                LocaleHint: callerLocaleHint), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "FinalizeAcceptance: QuoteAccepted domain-event publish failed "
                + "(quote_id={QuoteId}, order_id={OrderId}). Notification subscribers "
                + "(spec 025) will not fire unless replayed.",
                quote.Id, conversion.OrderId);
        }

        return FinalizeResult.Success(quote.Id, conversion.OrderId!.Value);
    }
}

public sealed record FinalizeResult(
    bool IsSuccess,
    int StatusCode,
    QuoteReasonCode? ReasonCode,
    Guid? QuoteId,
    Guid? OrderId)
{
    public static FinalizeResult Success(Guid q, Guid o) => new(true, 200, null, q, o);
    public static FinalizeResult NotFound() => new(false, 404, QuoteReasonCode.QuoteNotFound, null, null);
    public static FinalizeResult InvalidState() => new(false, 409, QuoteReasonCode.QuoteInvalidStateForAction, null, null);
    public static FinalizeResult AlreadyDecided() => new(false, 409, QuoteReasonCode.QuoteAlreadyDecided, null, null);
    public static FinalizeResult Expired() => new(false, 409, QuoteReasonCode.QuoteExpired, null, null);
    public static FinalizeResult EligibilityRequired() => new(false, 422, QuoteReasonCode.QuoteEligibilityRequired, null, null);
    public static FinalizeResult TaxDrift() => new(false, 409, QuoteReasonCode.QuoteTaxPreviewDriftThresholdExceeded, null, null);
}

public sealed record FinalizeAcceptanceRequest(
    [property: JsonPropertyName("tax_preview_drift_acknowledged")] bool? TaxPreviewDriftAcknowledged);

public static class FinalizeAcceptanceEndpoint
{
    public static IEndpointRouteBuilder MapFinalizeAcceptanceEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/finalize-acceptance", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        [FromBody] FinalizeAcceptanceRequest? body,
        HttpContext context,
        FinalizeAcceptanceHandler handler,
        CancellationToken ct)
    {
        var customerId = B2BResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return B2BResponseFactory.Problem(context, 401,
                QuoteReasonCode.QuoteRequiredFieldMissing,
                "Authentication required.");
        }
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var idem)
            || idem.Count == 0 || string.IsNullOrWhiteSpace(idem[0])
            || !Guid.TryParse(idem[0], out var idemKey))
        {
            return B2BResponseFactory.Problem(context, 400,
                QuoteReasonCode.QuoteRequiredFieldMissing,
                "Idempotency-Key header is required.");
        }

        var localeHint = B2BResponseFactory.ResolveLocaleHint(context);
        var result = await handler.HandleAsync(
            id, customerId.Value, idemKey,
            body?.TaxPreviewDriftAcknowledged ?? false, ct, localeHint);

        if (result.IsSuccess)
        {
            return Results.Ok(new { id = result.QuoteId, order_id = result.OrderId, state = "accepted" });
        }
        return B2BResponseFactory.Problem(context, result.StatusCode, result.ReasonCode!.Value,
            "Finalize-acceptance rejected.");
    }
}
