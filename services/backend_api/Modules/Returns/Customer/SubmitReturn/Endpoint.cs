using System.Text.Json;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using BackendApi.Modules.Returns.Common;
using BackendApi.Modules.Returns.Entities;
using BackendApi.Modules.Returns.Persistence;
using BackendApi.Modules.Returns.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Returns.Customer.SubmitReturn;

public sealed record SubmitReturnLine(Guid OrderLineId, int Qty, string? LineReasonCode);

public sealed record SubmitReturnRequest(
    IReadOnlyList<SubmitReturnLine> Lines,
    string ReasonCode,
    string? CustomerNotes,
    IReadOnlyList<Guid>? PhotoIds);

public static class Endpoint
{
    private const int MaxPhotos = 5;

    public static IEndpointRouteBuilder MapSubmitReturnEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{orderId:guid}/returns", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    /// <summary>
    /// FR-001 / FR-005 / FR-024. Submits a customer return for a delivered order. Validates
    /// the return window (per-market policy + per-line zero-window override), per-line
    /// available qty (delivered − already-returned − cancelled), and photo count. On success:
    ///   • inserts <c>return_request</c> + <c>return_lines</c> with line pricing snapshot;
    ///   • binds caller-uploaded photos to the new request;
    ///   • emits <c>return.submitted</c> outbox event so spec 011 can advance refund_state.
    /// </summary>
    private static async Task<IResult> HandleAsync(
        Guid orderId,
        SubmitReturnRequest body,
        HttpContext context,
        ReturnsDbContext returnsDb,
        OrdersDbContext ordersDb,
        ReturnNumberSequencer sequencer,
        ReturnPolicyEvaluator policyEvaluator,
        CancellationToken ct)
    {
        var accountId = ReturnsResponseFactory.ResolveAccountId(context);
        if (accountId is null)
        {
            return ReturnsResponseFactory.Problem(context, 401, "returns.requires_auth", "Auth required");
        }
        if (body is null)
        {
            return ReturnsResponseFactory.Problem(context, 400, "return.invalid_request", "Body is required.");
        }
        if (string.IsNullOrWhiteSpace(body.ReasonCode))
        {
            return ReturnsResponseFactory.Problem(context, 400, "return.invalid_request", "reasonCode is required.");
        }
        if (body.Lines is null || body.Lines.Count == 0)
        {
            return ReturnsResponseFactory.Problem(context, 400, "return.invalid_request", "At least one line is required.");
        }
        // Reject duplicates inside the same payload up-front so the per-line caps below see a
        // clean view of requested qty.
        var dupGroups = body.Lines.GroupBy(l => l.OrderLineId).Where(g => g.Count() > 1).ToList();
        if (dupGroups.Count > 0)
        {
            return ReturnsResponseFactory.Problem(context, 400, "return.line.duplicate",
                $"Duplicate orderLineId in payload: {string.Join(",", dupGroups.Select(g => g.Key))}.");
        }
        var photoIds = body.PhotoIds ?? Array.Empty<Guid>();
        if (photoIds.Count > MaxPhotos)
        {
            return ReturnsResponseFactory.Problem(context, 400, "return.photos.too_many",
                $"At most {MaxPhotos} photos per request.");
        }

        var order = await ordersDb.Orders.AsNoTracking()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null || order.AccountId != accountId)
        {
            return ReturnsResponseFactory.Problem(context, 404, "return.not_found", "Order not found");
        }
        if (order.DeliveredAt is null
            || !string.Equals(order.FulfillmentState, FulfillmentSm.Delivered, StringComparison.OrdinalIgnoreCase))
        {
            return ReturnsResponseFactory.Problem(context, 409, "return.order.not_delivered",
                "Order is not delivered yet.");
        }

        // Deep-review pass 1 fix: serialise concurrent submits for the same order on a
        // 64-bit advisory lock derived from the orderId. Without this, two parallel customer
        // submits could each read identical "prior pending RMA" snapshots and both insert,
        // bypassing the per-line qty cap. The lock is held for the duration of the connection
        // (transaction-scoped advisory locks would be ideal but the ReturnsDbContext doesn't
        // own its own transaction here — single-statement ExecuteUpdateAsync is the heaviest
        // mutation, so a session-scoped lock with explicit unlock at end suffices).
        var lockKey = OrderAdvisoryLockKey(orderId);
        await returnsDb.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_lock({lockKey})", ct);
        try
        {
            return await SubmitInsideLockAsync(orderId, body, context, returnsDb, sequencer,
                policyEvaluator, accountId.Value, order, photoIds, ct);
        }
        finally
        {
            await returnsDb.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_unlock({lockKey})", ct);
        }
    }

    private static long OrderAdvisoryLockKey(Guid orderId)
    {
        Span<byte> buf = stackalloc byte[16];
        orderId.TryWriteBytes(buf);
        return BitConverter.ToInt64(buf[..8]);
    }

    private static async Task<IResult> SubmitInsideLockAsync(
        Guid orderId,
        SubmitReturnRequest body,
        HttpContext context,
        ReturnsDbContext returnsDb,
        ReturnNumberSequencer sequencer,
        ReturnPolicyEvaluator policyEvaluator,
        Guid accountId,
        BackendApi.Modules.Orders.Entities.Order order,
        IReadOnlyList<Guid> photoIds,
        CancellationToken ct)
    {

        // Pull the active market policy + per-line already-returned qty across prior RMAs so
        // we can do the per-line caps below in one shot.
        var policy = await returnsDb.ReturnPolicies.AsNoTracking()
            .FirstOrDefaultAsync(p => p.MarketCode == order.MarketCode, ct);
        if (policy is null)
        {
            return ReturnsResponseFactory.Problem(context, 422, "return.policy.missing",
                $"No return policy configured for market {order.MarketCode}.");
        }

        var orderLineIds = body.Lines.Select(l => l.OrderLineId).ToHashSet();
        // Sum requested qty per (orderLineId) across prior RMAs that haven't been rejected.
        // A rejected RMA's lines do NOT consume capacity. We cap on requested qty (not yet
        // approved) at the customer-submit boundary so two simultaneous customers can't each
        // "reserve" the same returnable units pre-approval; admin can still reject duplicates.
        var prior = await returnsDb.ReturnRequests.AsNoTracking()
            .Where(r => r.OrderId == orderId && r.State != ReturnStateMachine.Rejected)
            .SelectMany(r => r.Lines)
            .Where(l => orderLineIds.Contains(l.OrderLineId))
            .GroupBy(l => l.OrderLineId)
            .Select(g => new { OrderLineId = g.Key, RequestedQty = g.Sum(x => x.RequestedQty) })
            .ToDictionaryAsync(x => x.OrderLineId, x => x.RequestedQty, ct);

        var nowUtc = DateTimeOffset.UtcNow;
        var returnLines = new List<ReturnLine>(body.Lines.Count);
        foreach (var input in body.Lines)
        {
            if (input.Qty <= 0)
            {
                return ReturnsResponseFactory.Problem(context, 400, "return.invalid_request",
                    $"Line {input.OrderLineId} qty must be positive.");
            }
            var orderLine = order.Lines.FirstOrDefault(l => l.Id == input.OrderLineId);
            if (orderLine is null)
            {
                return ReturnsResponseFactory.Problem(context, 404, "return.line.not_found",
                    $"OrderLine {input.OrderLineId} not on order.");
            }
            // Available qty = qty − cancelled − returned − already-pending-RMA.
            // ReturnedQty is the cumulative count spec 011 has incremented after refunds; we
            // double-cap with the in-flight prior-RMA RequestedQty to stop pre-approval races.
            var alreadyPending = prior.GetValueOrDefault(orderLine.Id, 0);
            var available = orderLine.Qty - orderLine.CancelledQty - orderLine.ReturnedQty - alreadyPending;
            if (available <= 0 || input.Qty > available)
            {
                return ReturnsResponseFactory.Problem(context, 400, "return.line.qty_exceeds_delivered",
                    $"Line {orderLine.Id}: requested {input.Qty} exceeds available {available}.");
            }
            // FR-002 — restricted/sealed-pharma items have a 0-day window.
            var policyDecision = policyEvaluator.Evaluate(new PolicyEvaluationInput(
                DeliveredAt: order.DeliveredAt,
                ProductZeroWindow: orderLine.Restricted,
                ReturnWindowDays: policy.ReturnWindowDays,
                NowUtc: nowUtc));
            if (!policyDecision.Allowed)
            {
                return ReturnsResponseFactory.Problem(context, 400, policyDecision.ReasonCode!, policyDecision.Detail!);
            }
            returnLines.Add(new ReturnLine
            {
                Id = Guid.NewGuid(),
                OrderLineId = orderLine.Id,
                RequestedQty = input.Qty,
                LineReasonCode = input.LineReasonCode,
                UnitPriceMinor = orderLine.UnitPriceMinor,
                OriginalDiscountMinor = orderLine.LineDiscountMinor,
                OriginalTaxMinor = orderLine.LineTaxMinor,
                TaxRateBp = ResolveTaxRateBp(orderLine.LineTaxMinor, orderLine.UnitPriceMinor, orderLine.Qty, orderLine.LineDiscountMinor),
                OriginalQty = orderLine.Qty,
            });
        }

        // Verify caller-supplied photoIds belong to this account and are not already bound.
        if (photoIds.Count > 0)
        {
            var photos = await returnsDb.ReturnPhotos
                .Where(p => photoIds.Contains(p.Id))
                .ToListAsync(ct);
            if (photos.Count != photoIds.Count)
            {
                return ReturnsResponseFactory.Problem(context, 404, "return.photo.not_found",
                    "One or more photoIds were not found.");
            }
            if (photos.Any(p => p.AccountId != accountId))
            {
                return ReturnsResponseFactory.Problem(context, 403, "return.photo.forbidden",
                    "Cannot use photo uploaded by a different account.");
            }
            if (photos.Any(p => p.ReturnRequestId is not null))
            {
                return ReturnsResponseFactory.Problem(context, 409, "return.photo.already_bound",
                    "Photo already bound to another return.");
            }
        }

        var returnNumber = await sequencer.NextAsync(order.MarketCode, nowUtc, ct);
        var returnRequest = new ReturnRequest
        {
            Id = Guid.NewGuid(),
            ReturnNumber = returnNumber,
            OrderId = orderId,
            AccountId = accountId,
            MarketCode = order.MarketCode,
            State = ReturnStateMachine.PendingReview,
            SubmittedAt = nowUtc,
            ReasonCode = body.ReasonCode,
            CustomerNotes = body.CustomerNotes,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
        foreach (var line in returnLines)
        {
            line.ReturnRequestId = returnRequest.Id;
            returnRequest.Lines.Add(line);
        }
        returnsDb.ReturnRequests.Add(returnRequest);

        returnsDb.StateTransitions.Add(new ReturnStateTransition
        {
            ReturnRequestId = returnRequest.Id,
            Machine = ReturnStateTransition.MachineReturn,
            FromState = string.Empty,
            ToState = ReturnStateMachine.PendingReview,
            ActorAccountId = accountId,
            Trigger = "customer.submit",
            OccurredAt = nowUtc,
        });
        returnsDb.Outbox.Add(new ReturnsOutboxEntry
        {
            EventType = "return.submitted",
            AggregateId = returnRequest.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                returnRequestId = returnRequest.Id,
                returnNumber,
                orderId,
                accountId = accountId,
                marketCode = order.MarketCode,
            }),
            CommittedAt = nowUtc,
        });

        // Deep-review pass 2 fix: wrap return-request insert + photo binding in one tx so a
        // SaveChanges failure cannot leave photos bound to a non-existent return_request.
        // ExecuteUpdateAsync runs raw SQL outside change tracking; without an explicit tx the
        // bind would commit ahead of the request insert.
        await using var tx = await returnsDb.Database.BeginTransactionAsync(ct);
        await returnsDb.SaveChangesAsync(ct);
        if (photoIds.Count > 0)
        {
            await returnsDb.ReturnPhotos
                .Where(p => photoIds.Contains(p.Id) && p.ReturnRequestId == null && p.AccountId == accountId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ReturnRequestId, returnRequest.Id), ct);
        }
        await tx.CommitAsync(ct);

        return Results.Json(new
        {
            id = returnRequest.Id,
            returnNumber,
            state = returnRequest.State,
        }, statusCode: 201);
    }

    /// <summary>
    /// Reverse-engineer the tax-rate basis points from the line tax + line subtotal so we
    /// don't need to recompute against external pricing data. Falls back to 0 when the line
    /// had no taxable amount (defensive — the calculator handles the 0-rate case correctly).
    /// </summary>
    public static int ResolveTaxRateBp(long lineTaxMinor, long unitPriceMinor, int qty, long lineDiscountMinor)
    {
        var taxableBase = unitPriceMinor * qty - lineDiscountMinor;
        if (taxableBase <= 0) return 0;
        // bp = round(taxMinor * 10000 / taxableBase). Round half-up.
        var raw = (lineTaxMinor * 10_000L + (taxableBase / 2)) / taxableBase;
        if (raw < 0) raw = 0;
        if (raw > 10_000) raw = 10_000;
        return (int)raw;
    }
}
