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
        if (!await AdminMutation.LockReturnRequestAsync(db, id, ct))
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 404, "return.not_found", "Return not found.");
        }
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

        // CR Critical round 5: every received line MUST have an inspection entry.
        // Without this guard, a line with ReceivedQty>0 omitted from body.Lines would be
        // defaulted to sellable=0/defective=0 below — silently writing off the received
        // units while the request transitions to `inspected`. Fail fast instead.
        var requestedIds = requested.Select(x => x.Id).ToHashSet();
        foreach (var rl in r.Lines.Where(l => (l.ReceivedQty ?? 0) > 0))
        {
            if (!requestedIds.Contains(rl.Id))
            {
                await tx.RollbackAsync(ct);
                return ReturnsResponseFactory.Problem(context, 400, "inspection.missing_line",
                    $"ReturnLine {rl.Id} has receivedQty {rl.ReceivedQty} and must have an inspection entry.");
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

        // CR Major round 5: build the (warehouse, batch) consumption ledger ONCE up front
        // and decrement as each return line allocates from it. Previously sourceBuckets was
        // rebuilt per-line from the full product-level movement set, so two return lines
        // sharing the same ProductId could each consume the same buckets and double-count
        // the source stock.
        var orderLineIds = r.Lines.Select(l => l.OrderLineId).ToHashSet();
        var orderProducts = await ordersDb.OrderLines.AsNoTracking()
            .Where(ol => orderLineIds.Contains(ol.Id))
            .Select(ol => new { ol.Id, ol.OrderId, ol.ProductId })
            .ToListAsync(ct);
        var saleMovements = await inventoryDb.InventoryMovements.AsNoTracking()
            .Where(m => m.SourceKind == "order" && m.SourceId == r.OrderId && m.Kind == "sale")
            .Select(m => new { m.ProductId, m.WarehouseId, m.BatchId, m.Delta })
            .ToListAsync(ct);
        // Mutable shared ledger keyed by (productId, warehouseId, batchId-or-null).
        var consumption = saleMovements
            .GroupBy(s => (s.ProductId, s.WarehouseId, s.BatchId))
            .ToDictionary(
                g => g.Key,
                g => -g.Sum(x => x.Delta));   // sale Delta is negative; flip sign

        var pendingInventoryItems = new List<ReservationReturnLine>();
        var totalSellable = r.Lines.Sum(l => l.SellableQty ?? 0);
        if (totalSellable > 0)
        {
            foreach (var rl in r.Lines.Where(l => (l.SellableQty ?? 0) > 0))
            {
                var prod = orderProducts.FirstOrDefault(p => p.Id == rl.OrderLineId);
                if (prod is null)
                {
                    logger.LogWarning("returns.inspect.product_not_found returnLineId={Id} orderLineId={Order}",
                        rl.Id, rl.OrderLineId);
                    continue;
                }
                // Walk this product's buckets in source order, deducting from the SHARED
                // consumption ledger so a sibling return line for the same product can't
                // re-consume the same units. Buckets are sorted deterministically by
                // (warehouse, batch) for stable allocation across runs.
                var productBuckets = consumption
                    .Where(kv => kv.Key.ProductId == prod.ProductId && kv.Value > 0)
                    .OrderBy(kv => kv.Key.WarehouseId)
                    .ThenBy(kv => kv.Key.BatchId)
                    .ToList();
                if (productBuckets.Count == 0)
                {
                    logger.LogWarning(
                        "returns.inspect.no_source_movement returnLineId={Id} productId={Pid} orderId={OrderId} "
                        + "— sellable qty {Qty} will not restock.",
                        rl.Id, prod.ProductId, r.OrderId, rl.SellableQty);
                    continue;
                }
                var remaining = rl.SellableQty!.Value;
                foreach (var bucket in productBuckets)
                {
                    if (remaining <= 0) break;
                    var take = Math.Min(remaining, bucket.Value);
                    pendingInventoryItems.Add(new ReservationReturnLine(
                        ProductId: prod.ProductId,
                        WarehouseId: bucket.Key.WarehouseId,
                        BatchId: bucket.Key.BatchId,
                        Qty: take));
                    consumption[bucket.Key] -= take;
                    remaining -= take;
                }
                if (remaining > 0 && pendingInventoryItems.Count > 0)
                {
                    // Customer is returning more than the per-line consumption ledger held
                    // — pile the leftover onto the last allocation; the inventory handler
                    // creates a synthetic restock batch if needed.
                    pendingInventoryItems[^1] = pendingInventoryItems[^1] with { Qty = pendingInventoryItems[^1].Qty + remaining };
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

        // CR Critical round 5: post inventory restock AFTER the returns transaction
        // commits. If we posted before commit and SaveChanges failed, inventory would
        // already be restocked while the return stayed uninspected (cross-module saga
        // break). Post-commit best-effort with a compensation outbox row mirrors spec
        // 011's Cancel/ReleaseInventoryAsync pattern.
        if (pendingInventoryItems.Count > 0)
        {
            try
            {
                var result = await reservationConverter.PostReturnAsync(
                    r.OrderId, actorId.Value, pendingInventoryItems, "returns.inspect", ct);
                if (!result.IsSuccess)
                {
                    logger.LogError("returns.inspect.inventory_post_failed returnId={Id} reason={Reason}",
                        r.Id, result.ReasonCode);
                    db.Outbox.Add(new ReturnsOutboxEntry
                    {
                        EventType = "inventory.return_post_failed",
                        AggregateId = r.Id,
                        MarketCode = r.MarketCode,
                        PayloadJson = JsonSerializer.Serialize(new
                        {
                            returnRequestId = r.Id,
                            orderId = r.OrderId,
                            reasonCode = result.ReasonCode,
                            items = pendingInventoryItems,
                        }),
                        CommittedAt = DateTimeOffset.UtcNow,
                    });
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "returns.inspect.inventory_post_threw returnId={Id}", r.Id);
                db.Outbox.Add(new ReturnsOutboxEntry
                {
                    EventType = "inventory.return_post_failed",
                    AggregateId = r.Id,
                    MarketCode = r.MarketCode,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        returnRequestId = r.Id,
                        orderId = r.OrderId,
                        error = ex.Message,
                        items = pendingInventoryItems,
                    }),
                    CommittedAt = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync(ct);
            }
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
