using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Returns.Admin.Common;
using BackendApi.Modules.Returns.Common;
using BackendApi.Modules.Returns.Persistence;
using BackendApi.Modules.Returns.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Returns.Admin.Approve;

public sealed record ApproveRequest(string? AdminNotes);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminApproveEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/approve", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("returns.review.write");
        return builder;
    }

    /// <summary>FR-006 / FR-012 / FR-019. Approves all requested lines at full qty.</summary>
    private static async Task<IResult> HandleAsync(
        Guid id,
        ApproveRequest? body,
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
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var r = await db.ReturnRequests.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null)
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 404, "return.not_found", "Return not found.");
        }

        const string Trigger = "admin.approve";
        if (await AdminMutation.WasAlreadyApplied(db, r.Id, Trigger, string.Empty, ct))
        {
            await tx.RollbackAsync(ct);
            return Results.Ok(new { id = r.Id, state = r.State, deduped = true });
        }

        var fromState = r.State;
        if (!AdminMutation.ValidateTransition(fromState, ReturnStateMachine.Approved))
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 409, "return.state.illegal_transition",
                $"Cannot approve from state {fromState}.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        r.State = ReturnStateMachine.Approved;
        r.DecidedAt = nowUtc;
        r.DecidedByAccountId = actorId;
        r.AdminNotes = body?.AdminNotes ?? r.AdminNotes;
        r.UpdatedAt = nowUtc;
        foreach (var line in r.Lines)
        {
            line.ApprovedQty = line.RequestedQty;
        }

        db.StateTransitions.Add(AdminMutation.NewReturnTransition(
            r.Id, fromState, r.State, actorId.Value, Trigger, string.Empty,
            new { adminNotes = body?.AdminNotes }, nowUtc));
        db.Outbox.Add(AdminMutation.NewOutbox("return.approved", r.Id, new
        {
            returnRequestId = r.Id,
            returnNumber = r.ReturnNumber,
            orderId = r.OrderId,
            decidedByAccountId = actorId.Value,
        }, nowUtc));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await AdminMutation.PublishAuditAsync(auditPublisher, actorId.Value, "returns.approve",
            r.Id, new { state = fromState }, new { state = r.State }, body?.AdminNotes, ct);

        return Results.Ok(new { id = r.Id, state = r.State });
    }
}
