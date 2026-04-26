using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Returns.Admin.Common;
using BackendApi.Modules.Returns.Common;
using BackendApi.Modules.Returns.Persistence;
using BackendApi.Modules.Returns.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Returns.Admin.ApprovePartial;

public sealed record ApprovePartialLine(Guid ReturnLineId, int ApprovedQty);
public sealed record ApprovePartialRequest(IReadOnlyList<ApprovePartialLine> Lines, string? AdminNotes);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminApprovePartialEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/approve-partial", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("returns.review.write");
        return builder;
    }

    /// <summary>
    /// FR-006. Partial approval: each requested line gets an approvedQty in [0, requestedQty].
    /// Setting approvedQty=0 effectively drops the line. If no line ends up with approved&gt;0,
    /// the call is rejected as <c>return.partial.no_approved_lines</c> — admin should call
    /// <c>/reject</c> in that case.
    /// </summary>
    private static async Task<IResult> HandleAsync(
        Guid id,
        ApprovePartialRequest body,
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
        if (!await AdminMutation.LockReturnRequestAsync(db, id, ct))
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 404, "return.not_found", "Return not found.");
        }
        var r = await db.ReturnRequests.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null)
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 404, "return.not_found", "Return not found.");
        }

        var fromState = r.State;
        if (!AdminMutation.ValidateTransition(fromState, ReturnStateMachine.ApprovedPartial))
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 409, "return.state.illegal_transition",
                $"Cannot approve-partial from state {fromState}.");
        }

        var lineLookup = r.Lines.ToDictionary(l => l.Id);
        // Validate input first — short-circuit before mutating.
        var requested = body.Lines.GroupBy(l => l.ReturnLineId).Select(g => new { Id = g.Key, Qty = g.Sum(x => x.ApprovedQty) }).ToList();
        foreach (var line in requested)
        {
            if (!lineLookup.TryGetValue(line.Id, out var rl))
            {
                await tx.RollbackAsync(ct);
                return ReturnsResponseFactory.Problem(context, 404, "return.line.not_found",
                    $"ReturnLine {line.Id} not on request.");
            }
            if (line.Qty < 0 || line.Qty > rl.RequestedQty)
            {
                await tx.RollbackAsync(ct);
                return ReturnsResponseFactory.Problem(context, 400, "return.invalid_request",
                    $"Line {line.Id}: approvedQty {line.Qty} out of [0,{rl.RequestedQty}].");
            }
        }
        // CR Minor round 3: include normalized AdminNotes in the discriminator so retries
        // with different notes are not silently coalesced.
        var disc = string.Join("|", requested.OrderBy(x => x.Id).Select(x => $"{x.Id}={x.Qty}"))
            + "|notes=" + (body.AdminNotes ?? string.Empty).Trim();
        const string Trigger = "admin.approve_partial";
        if (await AdminMutation.WasAlreadyApplied(db, r.Id, Trigger, disc, ct))
        {
            await tx.RollbackAsync(ct);
            return Results.Ok(new { id = r.Id, state = r.State, deduped = true });
        }

        // Apply: lines NOT mentioned in payload are dropped (approvedQty=0).
        var receivedIds = requested.Select(x => x.Id).ToHashSet();
        foreach (var rl in r.Lines)
        {
            var match = requested.FirstOrDefault(x => x.Id == rl.Id);
            rl.ApprovedQty = match?.Qty ?? 0;
        }
        if (r.Lines.All(l => (l.ApprovedQty ?? 0) == 0))
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 400, "return.partial.no_approved_lines",
                "approve-partial must approve at least one line with qty>0; use /reject otherwise.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        r.State = ReturnStateMachine.ApprovedPartial;
        r.DecidedAt = nowUtc;
        r.DecidedByAccountId = actorId;
        r.AdminNotes = body.AdminNotes ?? r.AdminNotes;
        r.UpdatedAt = nowUtc;

        db.StateTransitions.Add(AdminMutation.NewReturnTransition(
            r.Id, r.MarketCode, fromState, r.State, actorId.Value, Trigger, disc,
            new { lines = requested, adminNotes = body.AdminNotes }, nowUtc));
        db.Outbox.Add(AdminMutation.NewOutbox("return.approved_partial", r.Id, r.MarketCode, new
        {
            returnRequestId = r.Id,
            returnNumber = r.ReturnNumber,
            orderId = r.OrderId,
            decidedByAccountId = actorId.Value,
            lines = r.Lines.Select(l => new { id = l.Id, approvedQty = l.ApprovedQty }),
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

        await AdminMutation.PublishAuditAsync(auditPublisher, actorId.Value, "returns.approve_partial",
            r.Id, new { state = fromState }, new { state = r.State, lines = requested }, body.AdminNotes, ct);

        return Results.Ok(new { id = r.Id, state = r.State });
    }
}
