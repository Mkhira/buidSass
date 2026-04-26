using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Returns.Admin.Common;
using BackendApi.Modules.Returns.Common;
using BackendApi.Modules.Returns.Persistence;
using BackendApi.Modules.Returns.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Returns.Admin.ForceRefund;

public sealed record ForceRefundRequest(string ReasonCode);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminForceRefundEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/force-refund", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("returns.refund.write");
        return builder;
    }

    /// <summary>
    /// FR-006 / US-6. Marks the return as <c>force_refund=true</c> and transitions it from
    /// <c>pending_review</c> directly toward <c>refunded</c> via the standard
    /// <c>/issue-refund</c> path (which respects the flag and uses approvedQty rather than
    /// sellableQty). NO inventory restock — that's the whole point of skip-physical.
    /// </summary>
    private static async Task<IResult> HandleAsync(
        Guid id,
        ForceRefundRequest body,
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
        var r = await db.ReturnRequests.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null)
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 404, "return.not_found", "Return not found.");
        }
        if (!string.Equals(r.State, ReturnStateMachine.PendingReview, StringComparison.OrdinalIgnoreCase))
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 409, "return.state.illegal_transition",
                $"force-refund only valid from pending_review (current: {r.State}).");
        }
        const string Trigger = "admin.force_refund";
        if (await AdminMutation.WasAlreadyApplied(db, r.Id, Trigger, body.ReasonCode, ct))
        {
            await tx.RollbackAsync(ct);
            return Results.Ok(new { id = r.Id, state = r.State, deduped = true });
        }

        var nowUtc = DateTimeOffset.UtcNow;
        // Flip force-refund + auto-approve all requested qty so the issue-refund path can run.
        r.ForceRefund = true;
        r.AdminNotes = $"force-refund:{body.ReasonCode}";
        r.DecidedAt = nowUtc;
        r.DecidedByAccountId = actorId;
        r.UpdatedAt = nowUtc;
        foreach (var rl in r.Lines)
        {
            rl.ApprovedQty = rl.RequestedQty;
        }
        db.StateTransitions.Add(AdminMutation.NewReturnTransition(
            r.Id, r.MarketCode, r.State, r.State, actorId.Value, Trigger, body.ReasonCode,
            new { reasonCode = body.ReasonCode }, nowUtc));
        db.Outbox.Add(AdminMutation.NewOutbox("return.force_refund_marked", r.Id, r.MarketCode, new
        {
            returnRequestId = r.Id,
            returnNumber = r.ReturnNumber,
            orderId = r.OrderId,
            reasonCode = body.ReasonCode,
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

        await AdminMutation.PublishAuditAsync(auditPublisher, actorId.Value, "returns.force_refund_marked",
            r.Id, null, new { forceRefund = true, reasonCode = body.ReasonCode }, body.ReasonCode, ct);

        return Results.Ok(new
        {
            id = r.Id,
            state = r.State,
            forceRefund = true,
            nextStep = "POST /v1/admin/returns/{id}/issue-refund",
        });
    }
}
