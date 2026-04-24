using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Inventory.Admin.Common;
using BackendApi.Modules.Inventory.Entities;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Inventory.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackendApi.Modules.Inventory.Admin.Batches;

public sealed record CreateBatchRequest(
    Guid ProductId,
    Guid WarehouseId,
    string LotNo,
    DateOnly ExpiryDate,
    int Qty,
    string? Notes);

public sealed record PatchBatchRequest(
    string? LotNo,
    DateOnly? ExpiryDate,
    string? Status,
    string? Notes);

public sealed record BatchDto(
    Guid Id,
    Guid ProductId,
    Guid WarehouseId,
    string LotNo,
    DateOnly ExpiryDate,
    int QtyOnHand,
    string Status,
    DateTimeOffset ReceivedAt,
    string? Notes);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapBatchEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/batches");
        var adminAuth = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };

        group.MapGet("", ListAsync).RequireAuthorization(adminAuth).RequirePermission("inventory.batch.read");
        group.MapGet("/{id:guid}", GetAsync).RequireAuthorization(adminAuth).RequirePermission("inventory.batch.read");
        group.MapPost("", CreateAsync).RequireAuthorization(adminAuth).RequirePermission("inventory.batch.write");
        group.MapPatch("/{id:guid}", PatchAsync).RequireAuthorization(adminAuth).RequirePermission("inventory.batch.write");

        return builder;
    }

    private static async Task<IResult> ListAsync(
        Guid? productId,
        Guid? warehouseId,
        string? status,
        InventoryDbContext db,
        CancellationToken ct)
    {
        var query = db.InventoryBatches.AsNoTracking().AsQueryable();

        if (productId.HasValue && productId.Value != Guid.Empty)
        {
            query = query.Where(x => x.ProductId == productId.Value);
        }

        if (warehouseId.HasValue && warehouseId.Value != Guid.Empty)
        {
            query = query.Where(x => x.WarehouseId == warehouseId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == status);
        }

        var rows = await query
            .OrderBy(x => x.ExpiryDate)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

        return Results.Ok(rows.Select(ToDto));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext context, InventoryDbContext db, CancellationToken ct)
    {
        var batch = await db.InventoryBatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (batch is null)
        {
            return AdminInventoryResponseFactory.Problem(
                context,
                404,
                "inventory.batch.not_found",
                "Batch not found",
                "The requested inventory batch does not exist.");
        }

        return Results.Ok(ToDto(batch));
    }

    private static async Task<IResult> CreateAsync(
        CreateBatchRequest request,
        HttpContext context,
        InventoryDbContext db,
        AtsCalculator atsCalculator,
        BucketMapper bucketMapper,
        AvailabilityEventEmitter availabilityEventEmitter,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken ct)
    {
        if (request.ProductId == Guid.Empty || request.WarehouseId == Guid.Empty)
        {
            return AdminInventoryResponseFactory.Problem(
                context,
                400,
                "inventory.invalid_items",
                "Invalid identifiers",
                "Product and warehouse ids are required.");
        }

        if (string.IsNullOrWhiteSpace(request.LotNo))
        {
            return AdminInventoryResponseFactory.Problem(
                context,
                400,
                "inventory.invalid_items",
                "Invalid lot number",
                "Lot number is required.");
        }

        if (request.Qty < 0)
        {
            return AdminInventoryResponseFactory.Problem(
                context,
                400,
                "inventory.batch_qty_negative",
                "Negative batch quantity",
                "Batch quantity cannot be negative.");
        }

        // Validate warehouse exists + resolve its market. Cross-schema FK to inventory.warehouses
        // isn't modeled (catalog products also aren't FK-tied to pricing); enforce at write time.
        var warehouseMarket = await db.Warehouses
            .AsNoTracking()
            .Where(w => w.Id == request.WarehouseId)
            .Select(w => w.MarketCode)
            .SingleOrDefaultAsync(ct);
        if (warehouseMarket is null)
        {
            return AdminInventoryResponseFactory.Problem(
                context,
                404,
                "inventory.warehouse.not_found",
                "Warehouse not found",
                "The requested warehouse does not exist.");
        }

        var actorId = AdminInventoryResponseFactory.ResolveActorAccountId(context);
        var nowUtc = DateTimeOffset.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var stock = await db.StockLevels
            .FromSqlInterpolated($"""
                SELECT *
                FROM inventory.stock_levels
                WHERE "ProductId" = {request.ProductId}
                  AND "WarehouseId" = {request.WarehouseId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(ct);

        if (stock is null)
        {
            stock = new StockLevel
            {
                ProductId = request.ProductId,
                WarehouseId = request.WarehouseId,
                OnHand = 0,
                Reserved = 0,
                SafetyStock = 0,
                ReorderThreshold = 0,
                BucketCache = "out_of_stock",
                UpdatedAt = nowUtc,
            };
            db.StockLevels.Add(stock);
        }

        var batch = new InventoryBatch
        {
            Id = Guid.NewGuid(),
            ProductId = request.ProductId,
            WarehouseId = request.WarehouseId,
            MarketCode = warehouseMarket,
            LotNo = request.LotNo.Trim(),
            ExpiryDate = request.ExpiryDate,
            QtyOnHand = request.Qty,
            Status = "active",
            ReceivedAt = nowUtc,
            ReceivedByAccountId = actorId == Guid.Empty ? null : actorId,
            Notes = request.Notes,
        };

        db.InventoryBatches.Add(batch);

        var bucketBefore = stock.BucketCache;
        stock.OnHand += request.Qty;
        stock.UpdatedAt = nowUtc;
        stock.BucketCache = bucketMapper.Map(atsCalculator.Compute(stock.OnHand, stock.Reserved, stock.SafetyStock));

        await availabilityEventEmitter.EmitIfChangedAsync(
            stock.ProductId,
            stock.WarehouseId,
            bucketBefore,
            stock.BucketCache,
            nowUtc,
            ct);

        var movement = new InventoryMovement
        {
            ProductId = request.ProductId,
            WarehouseId = request.WarehouseId,
            MarketCode = warehouseMarket,
            BatchId = batch.Id,
            Kind = "receipt",
            Delta = request.Qty,
            Reason = "inventory.batch.receipt",
            SourceKind = "manual",
            SourceId = null,
            ActorAccountId = actorId == Guid.Empty ? null : actorId,
            OccurredAt = nowUtc,
        };
        db.InventoryMovements.Add(movement);

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await tx.RollbackAsync(ct);
            return AdminInventoryResponseFactory.Problem(
                context,
                409,
                "inventory.batch.duplicate_lot",
                "Duplicate lot",
                "A batch with the same lot number already exists for this product and warehouse.");
        }

        if (actorId != Guid.Empty)
        {
            await auditEventPublisher.PublishAsync(new AuditEvent(
                actorId,
                "admin",
                "inventory.batch.created",
                nameof(InventoryBatch),
                batch.Id,
                null,
                new
                {
                    batch.ProductId,
                    batch.WarehouseId,
                    batch.LotNo,
                    batch.ExpiryDate,
                    batch.QtyOnHand,
                    movement.Id,
                },
                "inventory.batch.create"), ct);
        }

        return Results.Created($"/v1/admin/inventory/batches/{batch.Id:D}", new { id = batch.Id });
    }

    private static async Task<IResult> PatchAsync(
        Guid id,
        PatchBatchRequest request,
        HttpContext context,
        InventoryDbContext db,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken ct)
    {
        var batch = await db.InventoryBatches.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (batch is null)
        {
            return AdminInventoryResponseFactory.Problem(
                context,
                404,
                "inventory.batch.not_found",
                "Batch not found",
                "The requested inventory batch does not exist.");
        }

        var before = new
        {
            batch.LotNo,
            batch.ExpiryDate,
            batch.Status,
            batch.Notes,
        };

        if (!string.IsNullOrWhiteSpace(request.LotNo))
        {
            batch.LotNo = request.LotNo.Trim();
        }

        if (request.ExpiryDate.HasValue)
        {
            batch.ExpiryDate = request.ExpiryDate.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var normalizedStatus = request.Status.Trim().ToLowerInvariant();
            if (normalizedStatus is not ("active" or "depleted" or "expired"))
            {
                return AdminInventoryResponseFactory.Problem(
                    context,
                    400,
                    "inventory.batch.invalid_status",
                    "Invalid batch status",
                    "Status must be one of: active, depleted, expired.");
            }
            batch.Status = normalizedStatus;
        }

        if (request.Notes is not null)
        {
            batch.Notes = request.Notes;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return AdminInventoryResponseFactory.Problem(
                context,
                409,
                "inventory.batch.duplicate_lot",
                "Duplicate lot",
                "A batch with the same lot number already exists for this product and warehouse.");
        }

        var actorId = AdminInventoryResponseFactory.ResolveActorAccountId(context);
        if (actorId != Guid.Empty)
        {
            await auditEventPublisher.PublishAsync(new AuditEvent(
                actorId,
                "admin",
                "inventory.batch.updated",
                nameof(InventoryBatch),
                batch.Id,
                before,
                new
                {
                    batch.LotNo,
                    batch.ExpiryDate,
                    batch.Status,
                    batch.Notes,
                },
                "inventory.batch.patch"), ct);
        }

        return Results.Ok(ToDto(batch));
    }

    private static BatchDto ToDto(InventoryBatch batch)
    {
        return new BatchDto(
            batch.Id,
            batch.ProductId,
            batch.WarehouseId,
            batch.LotNo,
            batch.ExpiryDate,
            batch.QtyOnHand,
            batch.Status,
            batch.ReceivedAt,
            batch.Notes);
    }
}
