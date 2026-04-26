using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Returns.Common;
using BackendApi.Modules.Returns.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Returns.Admin.GetReturn;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminGetReturnEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("returns.read");
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        HttpContext context,
        ReturnsDbContext db,
        CancellationToken ct)
    {
        // CR Major round 3: scope every read to the caller's market so a KSA admin token
        // cannot fetch an EG return by guessing its id (cross-market data exposure).
        // The market claim is set by the admin auth pipeline; null means platform-wide
        // (super-admin), which we keep unrestricted for legitimate ops use.
        var marketClaim = context.User.FindFirst("market_code")?.Value
            ?? context.User.FindFirst("market")?.Value;
        var query = db.ReturnRequests.AsNoTracking()
            .Include(x => x.Lines)
            .Include(x => x.Photos)
            .Include(x => x.Inspections).ThenInclude(i => i.Lines)
            .Include(x => x.Refunds).ThenInclude(rf => rf.Lines)
            .Where(x => x.Id == id);
        if (!string.IsNullOrWhiteSpace(marketClaim) && !string.Equals(marketClaim, "platform", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.MarketCode == marketClaim);
        }
        var r = await query.FirstOrDefaultAsync(ct);
        if (r is null)
        {
            return ReturnsResponseFactory.Problem(context, 404, "return.not_found", "Return not found.");
        }
        var timelineQuery = db.StateTransitions.AsNoTracking()
            .Where(t => t.ReturnRequestId == id);
        if (!string.IsNullOrWhiteSpace(marketClaim) && !string.Equals(marketClaim, "platform", StringComparison.OrdinalIgnoreCase))
        {
            timelineQuery = timelineQuery.Where(t => t.MarketCode == marketClaim);
        }
        var timeline = await timelineQuery
            .OrderBy(t => t.OccurredAt)
            .Select(t => new
            {
                machine = t.Machine,
                from = t.FromState,
                to = t.ToState,
                actor = t.ActorAccountId,
                trigger = t.Trigger,
                reason = t.Reason,
                at = t.OccurredAt,
            })
            .ToListAsync(ct);

        return Results.Ok(new
        {
            id = r.Id,
            returnNumber = r.ReturnNumber,
            orderId = r.OrderId,
            accountId = r.AccountId,
            marketCode = r.MarketCode,
            state = r.State,
            reasonCode = r.ReasonCode,
            customerNotes = r.CustomerNotes,
            adminNotes = r.AdminNotes,
            submittedAt = r.SubmittedAt,
            decidedAt = r.DecidedAt,
            decidedBy = r.DecidedByAccountId,
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
                unitPriceMinor = l.UnitPriceMinor,
                taxRateBp = l.TaxRateBp,
                originalDiscountMinor = l.OriginalDiscountMinor,
            }),
            photos = r.Photos.Select(p => new { id = p.Id, mime = p.Mime, sizeBytes = p.SizeBytes, sha256 = p.Sha256 }),
            inspections = r.Inspections.Select(ins => new
            {
                id = ins.Id,
                state = ins.State,
                inspectorAccountId = ins.InspectorAccountId,
                startedAt = ins.StartedAt,
                completedAt = ins.CompletedAt,
                lines = ins.Lines.Select(il => new
                {
                    returnLineId = il.ReturnLineId,
                    sellableQty = il.SellableQty,
                    defectiveQty = il.DefectiveQty,
                }),
            }),
            refunds = r.Refunds.Select(rf => new
            {
                id = rf.Id,
                state = rf.State,
                providerId = rf.ProviderId,
                amountMinor = rf.AmountMinor,
                currency = rf.Currency,
                attempts = rf.Attempts,
                gatewayRef = rf.GatewayRef,
                failureReason = rf.FailureReason,
                // CR Major: do not return the full IBAN to anyone with `returns.read`.
                // Mask all but the last 4 digits; full value is only available via the
                // confirm-bank-transfer audit row (gated behind `returns.refund.write`).
                manualIbanMasked = MaskIban(rf.ManualIban),
                initiatedAt = rf.InitiatedAt,
                completedAt = rf.CompletedAt,
                restockingFeeMinor = rf.RestockingFeeMinor,
            }),
            timeline,
        });
    }

    private static string? MaskIban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban)) return null;
        var s = iban.Replace(" ", string.Empty);
        return s.Length <= 4 ? new string('*', s.Length) : new string('*', s.Length - 4) + s[^4..];
    }
}
