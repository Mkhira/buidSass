using BackendApi.Modules.Returns.Common;
using BackendApi.Modules.Returns.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Returns.Customer.GetReturn;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapGetReturnEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    /// <summary>FR-013. Returns full detail + timeline (state-transition log).</summary>
    private static async Task<IResult> HandleAsync(
        Guid id,
        HttpContext context,
        ReturnsDbContext db,
        CancellationToken ct)
    {
        var accountId = ReturnsResponseFactory.ResolveAccountId(context);
        if (accountId is null)
        {
            return ReturnsResponseFactory.Problem(context, 401, "returns.requires_auth", "Auth required");
        }
        var r = await db.ReturnRequests.AsNoTracking()
            .Include(x => x.Lines)
            .Include(x => x.Photos)
            .Include(x => x.Refunds)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null || r.AccountId != accountId)
        {
            return ReturnsResponseFactory.Problem(context, 404, "return.not_found", "Return not found.");
        }
        var timeline = await db.StateTransitions.AsNoTracking()
            .Where(t => t.ReturnRequestId == id)
            .OrderBy(t => t.OccurredAt)
            .Select(t => new
            {
                machine = t.Machine,
                from = t.FromState,
                to = t.ToState,
                trigger = t.Trigger,
                at = t.OccurredAt,
            })
            .ToListAsync(ct);

        return Results.Ok(new
        {
            id = r.Id,
            returnNumber = r.ReturnNumber,
            orderId = r.OrderId,
            marketCode = r.MarketCode,
            state = r.State,
            reasonCode = r.ReasonCode,
            customerNotes = r.CustomerNotes,
            submittedAt = r.SubmittedAt,
            decidedAt = r.DecidedAt,
            forceRefund = r.ForceRefund,
            lines = r.Lines.Select(l => new
            {
                id = l.Id,
                orderLineId = l.OrderLineId,
                requestedQty = l.RequestedQty,
                approvedQty = l.ApprovedQty,
                receivedQty = l.ReceivedQty,
                sellableQty = l.SellableQty,
                defectiveQty = l.DefectiveQty,
                lineReasonCode = l.LineReasonCode,
            }),
            photos = r.Photos.Select(p => new { id = p.Id, mime = p.Mime, sizeBytes = p.SizeBytes }),
            refund = r.Refunds.OrderByDescending(rf => rf.InitiatedAt).Select(rf => new
            {
                id = rf.Id,
                state = rf.State,
                amountMinor = rf.AmountMinor,
                currency = rf.Currency,
                initiatedAt = rf.InitiatedAt,
                completedAt = rf.CompletedAt,
                attempts = rf.Attempts,
                providerId = rf.ProviderId,
                gatewayRef = rf.GatewayRef,
            }).FirstOrDefault(),
            timeline,
        });
    }
}
