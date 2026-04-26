using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Returns.Admin.Common;
using BackendApi.Modules.Returns.Common;
using BackendApi.Modules.Returns.Persistence;
using BackendApi.Modules.Returns.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Returns.Admin.MarkReceived;

public sealed record MarkReceivedLine(Guid ReturnLineId, int ReceivedQty);
public sealed record MarkReceivedRequest(IReadOnlyList<MarkReceivedLine> Lines);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminMarkReceivedEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/mark-received", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("returns.warehouse.write");
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        MarkReceivedRequest body,
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
        if (body is null || body.Lines is null || body.Lines.Count == 0)
        {
            return ReturnsResponseFactory.Problem(context, 400, "return.invalid_request", "lines is required.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var r = await db.ReturnRequests.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null)
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 404, "return.not_found", "Return not found.");
        }

        var fromState = r.State;
        if (!AdminMutation.ValidateTransition(fromState, ReturnStateMachine.Received))
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 409, "return.state.illegal_transition",
                $"Cannot mark-received from state {fromState}.");
        }

        var lookup = r.Lines.ToDictionary(l => l.Id);
        var requested = body.Lines.GroupBy(l => l.ReturnLineId)
            .Select(g => new { Id = g.Key, Qty = g.Sum(x => x.ReceivedQty) }).ToList();
        foreach (var line in requested)
        {
            if (!lookup.TryGetValue(line.Id, out var rl))
            {
                await tx.RollbackAsync(ct);
                return ReturnsResponseFactory.Problem(context, 404, "return.line.not_found",
                    $"ReturnLine {line.Id} not on request.");
            }
            var cap = rl.ApprovedQty ?? rl.RequestedQty;
            if (line.Qty < 0 || line.Qty > cap)
            {
                await tx.RollbackAsync(ct);
                return ReturnsResponseFactory.Problem(context, 400, "inspection.qty_mismatch",
                    $"Line {line.Id}: receivedQty {line.Qty} out of [0,{cap}].");
            }
        }

        var disc = string.Join("|", requested.OrderBy(x => x.Id).Select(x => $"{x.Id}={x.Qty}"));
        const string Trigger = "admin.mark_received";
        if (await AdminMutation.WasAlreadyApplied(db, r.Id, Trigger, disc, ct))
        {
            await tx.RollbackAsync(ct);
            return Results.Ok(new { id = r.Id, state = r.State, deduped = true });
        }

        foreach (var rl in r.Lines)
        {
            var match = requested.FirstOrDefault(x => x.Id == rl.Id);
            rl.ReceivedQty = match?.Qty ?? 0;
        }
        var nowUtc = DateTimeOffset.UtcNow;
        r.State = ReturnStateMachine.Received;
        r.UpdatedAt = nowUtc;
        db.StateTransitions.Add(AdminMutation.NewReturnTransition(
            r.Id, r.MarketCode, fromState, r.State, actorId.Value, Trigger, disc, new { lines = requested }, nowUtc));
        db.Outbox.Add(AdminMutation.NewOutbox("return.received", r.Id, r.MarketCode, new
        {
            returnRequestId = r.Id,
            returnNumber = r.ReturnNumber,
            orderId = r.OrderId,
            lines = requested,
        }, nowUtc));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await AdminMutation.PublishAuditAsync(auditPublisher, actorId.Value, "returns.mark_received",
            r.Id, new { state = fromState }, new { state = r.State, lines = requested }, null, ct);

        return Results.Ok(new { id = r.Id, state = r.State });
    }
}
