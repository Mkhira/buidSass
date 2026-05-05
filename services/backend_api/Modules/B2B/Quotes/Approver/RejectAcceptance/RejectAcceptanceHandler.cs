using System.Text.Json;
using System.Text.Json.Serialization;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromCart;
using BackendApi.Modules.Shared;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.B2B.Quotes.Approver.RejectAcceptance;

/// <summary>
/// Spec 021 contract §3.3 — approver rejects the buyer's acceptance.
/// State <c>pending-approver → revised</c>. <c>approver_rejection_note</c> set;
/// rejecting approver's identity captured.
/// </summary>
public sealed class RejectAcceptanceHandler
{
    private readonly B2BDbContext _db;
    private readonly IAuditEventPublisher _auditPublisher;
    private readonly IPublisher _domainPublisher;
    private readonly TimeProvider _time;

    public RejectAcceptanceHandler(
        B2BDbContext db,
        IAuditEventPublisher auditPublisher,
        IPublisher domainPublisher,
        TimeProvider time)
    {
        _db = db;
        _auditPublisher = auditPublisher;
        _domainPublisher = domainPublisher;
        _time = time;
    }

    public async Task<RejectResult> HandleAsync(
        Guid quoteId,
        Guid approverId,
        LocalizedMessage comment,
        CancellationToken ct)
    {
        var quote = await _db.Quotes.FirstOrDefaultAsync(q => q.Id == quoteId, ct);
        if (quote is null) return RejectResult.NotFound();
        if (quote.CompanyId is null) return RejectResult.NotFound();

        var hasApprover = await _db.CompanyMemberships.AsNoTracking()
            .AnyAsync(m => m.CompanyId == quote.CompanyId && m.UserId == approverId && m.Role == "approver", ct);
        if (!hasApprover) return RejectResult.NotFound();

        if (!QuoteStateExtensions.TryParseToken(quote.State, out var current)) return RejectResult.InvalidState();
        if (current != QuoteState.PendingApprover)
        {
            if (current == QuoteState.Accepted || current == QuoteState.Rejected
                || current == QuoteState.Expired || current == QuoteState.Withdrawn)
            {
                return RejectResult.AlreadyDecided();
            }
            return RejectResult.InvalidState();
        }

        var nowUtc = _time.GetUtcNow();
        var priorState = quote.State;
        quote.State = QuoteState.Revised.ToToken();
        quote.ApproverRejectionNote = JsonSerializer.Serialize(new
        {
            en = comment.En ?? "",
            ar = comment.Ar ?? "",
            rejected_by = approverId,
            rejected_at = nowUtc,
        });

        var transition = new QuoteStateTransition
        {
            Id = Guid.NewGuid(),
            QuoteId = quote.Id,
            MarketCode = quote.MarketCode,
            PriorState = priorState,
            NewState = quote.State,
            ActorKind = QuoteActorKind.Approver.ToToken(),
            ActorId = approverId,
            ReasonJson = JsonSerializer.Serialize(new { en = comment.En, ar = comment.Ar }),
            MetadataJson = "{}",
            OccurredAt = nowUtc,
        };
        _db.QuoteStateTransitions.Add(transition);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return RejectResult.AlreadyDecided();
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
                AfterState: new { state = quote.State },
                Reason: "approver_rejected"), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { }

        try
        {
            await _domainPublisher.Publish(new QuoteApproverRejected(
                QuoteId: quote.Id,
                BuyerUserId: quote.CustomerId,
                RejectingApproverUserId: approverId,
                MarketCode: quote.MarketCode), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { }

        return RejectResult.Success();
    }
}

public sealed record RejectResult(
    bool IsSuccess,
    int StatusCode,
    QuoteReasonCode? ReasonCode)
{
    public static RejectResult Success() => new(true, 200, null);
    public static RejectResult NotFound() => new(false, 404, QuoteReasonCode.QuoteNotFound);
    public static RejectResult InvalidState() => new(false, 409, QuoteReasonCode.QuoteInvalidStateForAction);
    public static RejectResult AlreadyDecided() => new(false, 409, QuoteReasonCode.QuoteAlreadyDecided);
    public static RejectResult ReasonRequired() => new(false, 400, QuoteReasonCode.QuoteReasonRequired);
}

public sealed record RejectAcceptanceRequest(
    [property: JsonPropertyName("comment")] LocalizedMessage? Comment);

public sealed class RejectAcceptanceValidator : AbstractValidator<RejectAcceptanceRequest>
{
    public RejectAcceptanceValidator()
    {
        RuleFor(x => x.Comment)
            .Must(c => c is not null && (!string.IsNullOrWhiteSpace(c.En) || !string.IsNullOrWhiteSpace(c.Ar)))
            .WithErrorCode(QuoteReasonCode.QuoteReasonRequired.ToToken())
            .WithMessage("comment must include at least one of {en, ar}");
    }
}

public static class RejectAcceptanceEndpoint
{
    public static IEndpointRouteBuilder MapRejectAcceptanceEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/reject-acceptance", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        [FromBody] RejectAcceptanceRequest? body,
        HttpContext context,
        RejectAcceptanceHandler handler,
        IValidator<RejectAcceptanceRequest> validator,
        CancellationToken ct)
    {
        var customerId = B2BResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return B2BResponseFactory.Problem(context, 401,
                QuoteReasonCode.QuoteRequiredFieldMissing,
                "Authentication required.");
        }
        body ??= new RejectAcceptanceRequest(null);
        var v = await validator.ValidateAsync(body, ct);
        if (!v.IsValid)
        {
            return Results.Json(new
            {
                type = "https://errors.dental-commerce/quotes/quote.reason_required",
                title = "Reason required.",
                status = 400,
                detail = v.Errors[0].ErrorMessage,
                instance = context.Request.Path.ToString(),
                reasonCode = v.Errors[0].ErrorCode,
            }, statusCode: 400, contentType: "application/problem+json");
        }
        var result = await handler.HandleAsync(id, customerId.Value, body.Comment!, ct);
        if (result.IsSuccess) return Results.Ok(new { id, state = "revised" });
        return B2BResponseFactory.Problem(context, result.StatusCode, result.ReasonCode!.Value, "Reject rejected.");
    }
}
