using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Returns.Admin.Common;
using BackendApi.Modules.Returns.Common;
using BackendApi.Modules.Returns.Persistence;
using BackendApi.Modules.Returns.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Returns.Admin.Reject;

public sealed record RejectRequest(string ReasonCode, string? AdminNotes);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminRejectEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/reject", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("returns.review.write");
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        RejectRequest body,
        HttpContext context,
        ReturnsDbContext db,
        IAuditEventPublisher auditPublisher,
        CancellationToken ct)
    {
        var actorId = ReturnsResponseFactory.ResolveAccountId(context);
        if (actorId is null)
        {
            return ReturnsResponseFactory.Problem(context, 401, "returns.requires_auth", "Auth required");
        }
        if (body is null || string.IsNullOrWhiteSpace(body.ReasonCode))
        {
            return ReturnsResponseFactory.Problem(context, 400, "return.invalid_request", "reasonCode is required.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var r = await db.ReturnRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null)
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 404, "return.not_found", "Return not found.");
        }

        const string Trigger = "admin.reject";
        // CR Minor round 3: include normalized AdminNotes so a retry with different notes
        // is not silently dropped as a dedup of the original mutation.
        var disc = $"{body.ReasonCode}|{(body.AdminNotes ?? string.Empty).Trim()}";
        if (await AdminMutation.WasAlreadyApplied(db, r.Id, Trigger, disc, ct))
        {
            await tx.RollbackAsync(ct);
            return Results.Ok(new { id = r.Id, state = r.State, deduped = true });
        }

        var fromState = r.State;
        if (!AdminMutation.ValidateTransition(fromState, ReturnStateMachine.Rejected))
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 409, "return.state.illegal_transition",
                $"Cannot reject from state {fromState}.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        r.State = ReturnStateMachine.Rejected;
        r.DecidedAt = nowUtc;
        r.DecidedByAccountId = actorId;
        r.AdminNotes = body.AdminNotes ?? r.AdminNotes;
        r.UpdatedAt = nowUtc;

        db.StateTransitions.Add(AdminMutation.NewReturnTransition(
            r.Id, r.MarketCode, fromState, r.State, actorId.Value, Trigger, disc,
            new { reasonCode = body.ReasonCode, adminNotes = body.AdminNotes }, nowUtc));
        db.Outbox.Add(AdminMutation.NewOutbox("return.rejected", r.Id, r.MarketCode, new
        {
            returnRequestId = r.Id,
            returnNumber = r.ReturnNumber,
            orderId = r.OrderId,
            reasonCode = body.ReasonCode,
            decidedByAccountId = actorId.Value,
        }, nowUtc));

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (AdminMutation.IsUniqueDedupViolation(ex))
        {
            await tx.RollbackAsync(ct);
            return Results.Ok(new { id = r.Id, state = r.State, deduped = true });
        }

        await AdminMutation.PublishAuditAsync(auditPublisher, actorId.Value, "returns.reject",
            r.Id, new { state = fromState }, new { state = r.State, reasonCode = body.ReasonCode }, body.ReasonCode, ct);

        return Results.Ok(new { id = r.Id, state = r.State });
    }
}
