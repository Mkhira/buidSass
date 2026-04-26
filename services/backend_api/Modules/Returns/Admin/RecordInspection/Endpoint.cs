using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Returns.Admin.Common;
using BackendApi.Modules.Returns.Common;
using BackendApi.Modules.Returns.Entities;
using BackendApi.Modules.Returns.Persistence;
using BackendApi.Modules.Returns.Primitives;
using BackendApi.Modules.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Returns.Admin.RecordInspection;

public sealed record RecordInspectionLine(Guid ReturnLineId, int SellableQty, int DefectiveQty);
public sealed record RecordInspectionRequest(IReadOnlyList<RecordInspectionLine> Lines);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminRecordInspectionEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/inspect", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("returns.warehouse.write");
        return builder;
    }

    /// <summary>
    /// FR-009 / SC-007. Records the inspection: per line <c>sellable_qty</c> + <c>defective_qty</c>
    /// must sum to <c>received_qty</c>. Sellable units post a single batched <c>kind=return</c>
    /// movement to spec 008 via <see cref="IReservationConverter.PostReturnAsync"/>; defective
    /// units stay out of ATS. Idempotent on the request payload — replay returns the same
    /// inspection record.
    /// </summary>
    private static async Task<IResult> HandleAsync(
        Guid id,
        RecordInspectionRequest body,
        HttpContext context,
        ReturnsDbContext db,
        OrdersDbContext ordersDb,
        InventoryDbContext inventoryDb,
        IReservationConverter reservationConverter,
        IAuditEventPublisher auditPublisher,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Returns.RecordInspection");
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
        var r = await db.ReturnRequests
            .Include(x => x.Lines)
            .Include(x => x.Inspections).ThenInclude(i => i.Lines)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null)
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 404, "return.not_found", "Return not found.");
        }

        var fromState = r.State;
        if (!AdminMutation.ValidateTransition(fromState, ReturnStateMachine.Inspected))
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 409, "return.state.illegal_transition",
                $"Cannot inspect from state {fromState}.");
        }

        var lookup = r.Lines.ToDictionary(l => l.Id);
        var requested = body.Lines.GroupBy(l => l.ReturnLineId)
            .Select(g => new
            {
                Id = g.Key,
                Sellable = g.Sum(x => x.SellableQty),
                Defective = g.Sum(x => x.DefectiveQty),
            })
            .ToList();

        foreach (var line in requested)
        {
            if (!lookup.TryGetValue(line.Id, out var rl))
            {
                await tx.RollbackAsync(ct);
                return ReturnsResponseFactory.Problem(context, 404, "return.line.not_found",
                    $"ReturnLine {line.Id} not on request.");
            }
            if (line.Sellable < 0 || line.Defective < 0)
            {
                await tx.RollbackAsync(ct);
                return ReturnsResponseFactory.Problem(context, 400, "inspection.qty_mismatch",
                    "Sellable / defective qty must be non-negative.");
            }
            if (rl.ReceivedQty is null)
            {
                await tx.RollbackAsync(ct);
                return ReturnsResponseFactory.Problem(context, 409, "inspection.qty_mismatch",
                    $"Line {line.Id}: receivedQty not set; mark-received first.");
            }
            if (line.Sellable + line.Defective != rl.ReceivedQty)
            {
                await tx.RollbackAsync(ct);
                return ReturnsResponseFactory.Problem(context, 400, "inspection.qty_mismatch",
                    $"Line {line.Id}: sellable {line.Sellable} + defective {line.Defective} != received {rl.ReceivedQty}.");
            }
        }

        var disc = string.Join("|", requested.OrderBy(x => x.Id).Select(x => $"{x.Id}=s{x.Sellable}d{x.Defective}"));
        const string Trigger = "admin.inspect";
        if (await AdminMutation.WasAlreadyApplied(db, r.Id, Trigger, disc, ct))
        {
            await tx.RollbackAsync(ct);
            return Results.Ok(new { id = r.Id, state = r.State, deduped = true });
        }

        // Apply inspection per line.
        foreach (var rl in r.Lines)
        {
            var match = requested.FirstOrDefault(x => x.Id == rl.Id);
            rl.SellableQty = match?.Sellable ?? 0;
            rl.DefectiveQty = match?.Defective ?? 0;
        }
        var nowUtc = DateTimeOffset.UtcNow;
        var inspection = new Inspection
        {
            Id = Guid.NewGuid(),
            ReturnRequestId = r.Id,
            MarketCode = r.MarketCode,
            InspectorAccountId = actorId.Value,
            State = InspectionStateMachine.Complete,
            StartedAt = nowUtc,
            CompletedAt = nowUtc,
        };
        foreach (var match in requested)
        {
            inspection.Lines.Add(new InspectionLine
            {
                InspectionId = inspection.Id,
                ReturnLineId = match.Id,
                MarketCode = r.MarketCode,
                SellableQty = match.Sellable,
                DefectiveQty = match.Defective,
            });
        }
        db.Inspections.Add(inspection);

        // Resolve (productId, warehouseId, batchId) per return line by reading the original
        // sale movement(s) recorded by spec 011's CreateFromCheckout flow. Sellable qty is
        // routed back into the same source bucket; the inventory return handler routes
        // expired batches to a fresh restock batch.
        var orderLineIds = r.Lines.Select(l => l.OrderLineId).ToHashSet();
        var orderProducts = await ordersDb.OrderLines.AsNoTracking()
            .Where(ol => orderLineIds.Contains(ol.Id))
            .Select(ol => new { ol.Id, ol.OrderId, ol.ProductId })
            .ToListAsync(ct);
        // CR Major fix: include Delta so we can fan-out the restock across the actual
        // (warehouse, batch) buckets in proportion to the original sale, instead of
        // collapsing to FirstOrDefault and routing the wrong warehouse/batch.
        var saleMovements = await inventoryDb.InventoryMovements.AsNoTracking()
            .Where(m => m.SourceKind == "order" && m.SourceId == r.OrderId && m.Kind == "sale")
            .Select(m => new { m.ProductId, m.WarehouseId, m.BatchId, m.Delta })
            .ToListAsync(ct);

        var totalSellable = r.Lines.Sum(l => l.SellableQty ?? 0);
        if (totalSellable > 0)
        {
            var items = new List<ReservationReturnLine>();
            foreach (var rl in r.Lines.Where(l => (l.SellableQty ?? 0) > 0))
            {
                var prod = orderProducts.FirstOrDefault(p => p.Id == rl.OrderLineId);
                if (prod is null)
                {
                    logger.LogWarning("returns.inspect.product_not_found returnLineId={Id} orderLineId={Order}",
                        rl.Id, rl.OrderLineId);
                    continue;
                }
                // Group all sale movements for this product into (warehouse, batch) buckets,
                // each carrying the absolute qty originally drawn. We then distribute the
                // sellable qty across those buckets in source order, preserving original
                // warehouse/batch fidelity. The inventory return handler routes expired
                // batches to a fresh restock batch independently.
                var sourceBuckets = saleMovements
                    .Where(s => s.ProductId == prod.ProductId)
                    .GroupBy(s => (s.WarehouseId, s.BatchId))
                    .Select(g => new
                    {
                        g.Key.WarehouseId,
                        g.Key.BatchId,
                        AvailableQty = -g.Sum(x => x.Delta), // sale Delta is negative
                    })
                    .Where(b => b.AvailableQty > 0)
                    .ToList();
                if (sourceBuckets.Count == 0)
                {
                    logger.LogWarning(
                        "returns.inspect.no_source_movement returnLineId={Id} productId={Pid} orderId={OrderId} "
                        + "— sellable qty {Qty} will not restock.",
                        rl.Id, prod.ProductId, r.OrderId, rl.SellableQty);
                    continue;
                }
                var remaining = rl.SellableQty!.Value;
                foreach (var bucket in sourceBuckets)
                {
                    if (remaining <= 0) break;
                    var take = Math.Min(remaining, bucket.AvailableQty);
                    items.Add(new ReservationReturnLine(
                        ProductId: prod.ProductId,
                        WarehouseId: bucket.WarehouseId,
                        BatchId: bucket.BatchId,
                        Qty: take));
                    remaining -= take;
                }
                if (remaining > 0)
                {
                    // Customer is returning more than was ever sold for this line — partial
                    // restock; remainder goes to the first available bucket (still preserves
                    // the original warehouse), then the inventory handler will create a
                    // synthetic restock batch if needed.
                    items[^1] = items[^1] with { Qty = items[^1].Qty + remaining };
                }
            }
            if (items.Count > 0)
            {
                var result = await reservationConverter.PostReturnAsync(
                    r.OrderId, actorId.Value, items, "returns.inspect", ct);
                if (!result.IsSuccess)
                {
                    logger.LogError("returns.inspect.inventory_post_failed returnId={Id} reason={Reason}",
                        r.Id, result.ReasonCode);
                    await tx.RollbackAsync(ct);
                    return ReturnsResponseFactory.Problem(context, 500, "inventory.post_failed",
                        $"Inventory return movement failed: {result.ReasonCode}");
                }
            }
        }

        r.State = ReturnStateMachine.Inspected;
        r.UpdatedAt = nowUtc;
        db.StateTransitions.Add(AdminMutation.NewReturnTransition(
            r.Id, r.MarketCode, fromState, r.State, actorId.Value, Trigger, disc,
            new
            {
                inspectionId = inspection.Id,
                lines = requested,
                totalSellable,
            }, nowUtc));
        db.StateTransitions.Add(new ReturnStateTransition
        {
            ReturnRequestId = r.Id,
            MarketCode = r.MarketCode,
            InspectionId = inspection.Id,
            Machine = ReturnStateTransition.MachineInspection,
            FromState = string.Empty,
            ToState = InspectionStateMachine.Complete,
            ActorAccountId = actorId.Value,
            Trigger = Trigger,
            Reason = disc,
            ContextJson = JsonSerializer.Serialize(new { totalSellable }),
            OccurredAt = nowUtc,
        });
        db.Outbox.Add(AdminMutation.NewOutbox("return.inspected", r.Id, r.MarketCode, new
        {
            returnRequestId = r.Id,
            returnNumber = r.ReturnNumber,
            orderId = r.OrderId,
            inspectionId = inspection.Id,
            totalSellable,
            lines = requested,
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
        await AdminMutation.PublishAuditAsync(auditPublisher, actorId.Value, "returns.inspect",
            r.Id, new { state = fromState }, new { state = r.State, inspectionId = inspection.Id }, null, ct);

        return Results.Ok(new
        {
            id = r.Id,
            state = r.State,
            inspectionId = inspection.Id,
            totalSellableUnits = totalSellable,
        });
    }
}
