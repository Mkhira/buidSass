using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Inventory.Internal.Reservations.Create;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Inventory.Primitives;
using BackendApi.Modules.Inventory.Primitives.Fefo;
using BackendApi.Modules.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Cart.Primitives;

public sealed record CartReservationResult(
    bool IsSuccess,
    int StatusCode,
    string? ReasonCode,
    string? Detail,
    Guid? ReservationId,
    IDictionary<string, object?>? Extensions = null);

/// <summary>
/// Thin wrapper over spec 008's in-process reservation handlers. Keeps the cart module
/// from needing HTTP self-calls to /v1/internal/inventory — we resolve the handler's deps
/// from DI and invoke it directly.
/// </summary>
public sealed class CartInventoryOrchestrator(
    AtsCalculator atsCalculator,
    BucketMapper bucketMapper,
    FefoPicker fefoPicker,
    ReorderAlertEmitter reorderAlertEmitter,
    AvailabilityEventEmitter availabilityEventEmitter,
    InventoryMetrics inventoryMetrics,
    IAuditEventPublisher auditEventPublisher,
    IOptions<InventoryOptions> inventoryOptions,
    ILoggerFactory loggerFactory)
{
    public async Task<CartReservationResult> TryReserveAsync(
        InventoryDbContext inventoryDb,
        CatalogDbContext catalogDb,
        Guid productId,
        int qty,
        string marketCode,
        Guid? accountId,
        Guid cartId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var request = new CreateReservationRequest(
            CartId: cartId,
            AccountId: accountId,
            MarketCode: marketCode,
            Items: new[] { new CreateReservationItem(productId, qty) });

        var outcome = await Inventory.Internal.Reservations.Create.Handler.HandleAsync(
            request,
            inventoryDb,
            catalogDb,
            atsCalculator,
            bucketMapper,
            fefoPicker,
            reorderAlertEmitter,
            availabilityEventEmitter,
            inventoryMetrics,
            auditEventPublisher,
            inventoryOptions,
            loggerFactory.CreateLogger("Cart.InventoryOrchestrator.Reserve"),
            cancellationToken);

        if (!outcome.IsSuccess)
        {
            // Re-map inventory reason codes to cart-facing reason codes where appropriate.
            var mapped = outcome.ReasonCode switch
            {
                "inventory.insufficient" => "cart.inventory_insufficient",
                "inventory.warehouse_market_mismatch" => "cart.product_market_mismatch",
                "inventory.invalid_qty" => "cart.below_min_qty",
                _ => outcome.ReasonCode ?? "cart.inventory_error",
            };
            return new CartReservationResult(
                false, outcome.StatusCode, mapped, outcome.Detail, null, outcome.Extensions);
        }

        return new CartReservationResult(true, 200, null, null, outcome.Response!.ReservationId);
    }

    public async Task<bool> TryReleaseAsync(
        InventoryDbContext inventoryDb,
        Guid reservationId,
        Guid actorId,
        string reason,
        CancellationToken cancellationToken)
    {
        var outcome = await Inventory.Internal.Reservations.Release.Handler.HandleAsync(
            reservationId,
            actorId,
            reason,
            inventoryDb,
            atsCalculator,
            bucketMapper,
            availabilityEventEmitter,
            auditEventPublisher,
            loggerFactory.CreateLogger("Cart.InventoryOrchestrator.Release"),
            cancellationToken);
        return outcome.IsSuccess;
    }
}
