using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Inventory.Primitives;
using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Orders.Internal.CreateFromCheckout;

/// <summary>
/// FR-001 / FR-013 / FR-014. The real implementation of <see cref="IOrderFromCheckoutHandler"/>;
/// replaces spec 010's <c>StubOrderFromCheckoutHandler</c>. Spec 010's Submit slice calls this
/// after authorizing payment; it MUST respect the pre-allocated order id (Pricing's Issue mode
/// already bound an explanation row to it) and is idempotent on retry.
///
/// Strategy on ordering: the order row is the source of truth. Inventory conversion runs
/// AFTER the order commits — if conversion fails, the order is still placed and the
/// fulfillment_state degrades to <c>awaiting_stock</c> so admin reconciliation can recover.
/// This mirrors spec 011 edge case 8 (quotation conversion races with inventory).
/// </summary>
public sealed class CreateFromCheckoutHandler(
    OrdersDbContext ordersDb,
    CatalogDbContext catalogDb,
    InventoryDbContext inventoryDb,
    OrderNumberSequencer sequencer,
    AtsCalculator atsCalculator,
    BucketMapper bucketMapper,
    ReorderAlertEmitter reorderAlertEmitter,
    AvailabilityEventEmitter availabilityEventEmitter,
    IAuditEventPublisher auditEventPublisher,
    ILogger<CreateFromCheckoutHandler> logger) : IOrderFromCheckoutHandler
{
    public async Task<OrderFromCheckoutResult> CreateAsync(OrderFromCheckoutRequest request, CancellationToken cancellationToken)
    {
        if (request.PreallocatedOrderId == Guid.Empty)
        {
            return Failure("orders.create.preallocated_id_missing", "Pre-allocated order id is required.");
        }
        if (string.IsNullOrWhiteSpace(request.MarketCode))
        {
            return Failure("orders.create.market_required", "Market code is required.");
        }
        if (request.Lines.Count == 0)
        {
            return Failure("orders.create.no_lines", "Order must have at least one line.");
        }

        // Idempotency: Submit may retry after a transient failure. If the order already exists
        // we return its current state — Submit's idempotency-key store will replay the same
        // response to the customer.
        var existing = await ordersDb.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == request.PreallocatedOrderId, cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation(
                "orders.create.idempotent_hit orderId={OrderId} orderNumber={OrderNumber}",
                existing.Id, existing.OrderNumber);
            return new OrderFromCheckoutResult(
                IsSuccess: true,
                OrderId: existing.Id,
                OrderNumber: existing.OrderNumber,
                PaymentState: MapPaymentStateToWire(existing.PaymentState));
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var initialPaymentState = ResolveInitialPaymentState(request.PaymentMethod);

        // Bulk snapshot from catalog — sku, names, restricted, attributes (research R6).
        var productIds = request.Lines.Select(l => l.ProductId).Distinct().ToArray();
        var catalogSnapshot = await catalogDb.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id, p.Sku, p.NameAr, p.NameEn, p.AttributesJson, p.Restricted,
            })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        // Order number is allocated from a per-(market, yyyymm) sequence. Generated BEFORE the
        // tx so a sequence-create cold-path doesn't deadlock against the order insert.
        string orderNumber;
        try
        {
            orderNumber = await sequencer.NextAsync(request.MarketCode, nowUtc, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "orders.create.order_number_failed orderId={OrderId} market={Market}",
                request.PreallocatedOrderId, request.MarketCode);
            return Failure("orders.create.order_number_failed", ex.Message);
        }

        var order = new Order
        {
            Id = request.PreallocatedOrderId,
            OrderNumber = orderNumber,
            AccountId = request.AccountId,
            MarketCode = request.MarketCode,
            Currency = request.Currency,
            SubtotalMinor = request.SubtotalMinor,
            DiscountMinor = request.DiscountMinor,
            TaxMinor = request.TaxMinor,
            ShippingMinor = request.ShippingFeeMinor,
            GrandTotalMinor = request.GrandTotalMinor,
            PriceExplanationId = request.IssuedExplanationId,
            CouponCode = request.CouponCode,
            ShippingAddressJson = string.IsNullOrWhiteSpace(request.ShippingAddressJson) ? "{}" : request.ShippingAddressJson,
            BillingAddressJson = string.IsNullOrWhiteSpace(request.BillingAddressJson) ? "{}" : request.BillingAddressJson,
            OrderState = OrderSm.Placed,
            PaymentState = initialPaymentState,
            FulfillmentState = FulfillmentSm.NotStarted,
            RefundState = RefundSm.None,
            PlacedAt = nowUtc,
            CheckoutSessionId = request.SessionId,
            PaymentProviderId = request.PaymentProviderId,
            PaymentProviderTxnId = request.PaymentProviderTxnId,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };

        foreach (var line in request.Lines)
        {
            catalogSnapshot.TryGetValue(line.ProductId, out var product);
            order.Lines.Add(new OrderLine
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ProductId = line.ProductId,
                Sku = product?.Sku ?? string.Empty,
                NameAr = product?.NameAr ?? string.Empty,
                NameEn = product?.NameEn ?? string.Empty,
                Qty = line.Qty,
                UnitPriceMinor = line.UnitPriceMinor,
                LineDiscountMinor = Math.Max(0, line.NetMinor < 0 ? 0 : (line.UnitPriceMinor * line.Qty - line.NetMinor)),
                LineTaxMinor = line.TaxMinor,
                LineTotalMinor = line.GrossMinor,
                Restricted = product?.Restricted ?? false,
                AttributesJson = product?.AttributesJson ?? "{}",
                CancelledQty = 0,
                ReturnedQty = 0,
                ReservationId = line.ReservationId,
            });
        }

        // FR-013 — emit order.placed transition + outbox row in the same SaveChanges.
        ordersDb.Orders.Add(order);
        ordersDb.StateTransitions.Add(new OrderStateTransition
        {
            OrderId = order.Id,
            Machine = OrderStateTransition.MachineOrder,
            FromState = string.Empty,
            ToState = OrderSm.Placed,
            ActorAccountId = request.AccountId,
            Trigger = "checkout.confirm",
            Reason = $"sessionId={request.SessionId}",
            OccurredAt = nowUtc,
        });
        ordersDb.StateTransitions.Add(new OrderStateTransition
        {
            OrderId = order.Id,
            Machine = OrderStateTransition.MachinePayment,
            FromState = string.Empty,
            ToState = initialPaymentState,
            ActorAccountId = request.AccountId,
            Trigger = "checkout.confirm",
            Reason = $"method={request.PaymentMethod}",
            OccurredAt = nowUtc,
        });
        ordersDb.Outbox.Add(new OrdersOutboxEntry
        {
            EventType = "order.placed",
            AggregateId = order.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                orderId = order.Id,
                orderNumber = order.OrderNumber,
                accountId = order.AccountId,
                market = order.MarketCode,
                grandTotalMinor = order.GrandTotalMinor,
                currency = order.Currency,
                paymentState = order.PaymentState,
                checkoutSessionId = order.CheckoutSessionId,
            }),
            CommittedAt = nowUtc,
            DispatchedAt = null,
        });

        // FR-015 / spec 012 seam: emit payment.captured immediately for synchronous-capture
        // methods so spec 012's invoice issuance fires without waiting for a webhook.
        if (string.Equals(initialPaymentState, PaymentSm.Captured, StringComparison.OrdinalIgnoreCase))
        {
            ordersDb.Outbox.Add(new OrdersOutboxEntry
            {
                EventType = "payment.captured",
                AggregateId = order.Id,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    orderId = order.Id,
                    orderNumber = order.OrderNumber,
                    capturedAmountMinor = order.GrandTotalMinor,
                    currency = order.Currency,
                    capturedAt = nowUtc,
                }),
                CommittedAt = nowUtc,
                DispatchedAt = null,
            });
        }

        try
        {
            await ordersDb.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            // Lost a race with a concurrent retry. Re-read the row and return as idempotent.
            var raced = await ordersDb.Orders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == request.PreallocatedOrderId, cancellationToken);
            if (raced is not null)
            {
                logger.LogInformation(
                    "orders.create.race_idempotent orderId={OrderId} orderNumber={OrderNumber}",
                    raced.Id, raced.OrderNumber);
                return new OrderFromCheckoutResult(true, raced.Id, raced.OrderNumber, MapPaymentStateToWire(raced.PaymentState));
            }
            logger.LogError(ex, "orders.create.duplicate_key orderId={OrderId}", request.PreallocatedOrderId);
            return Failure("orders.create.duplicate_key", ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "orders.create.persist_failed orderId={OrderId} sessionId={SessionId}",
                request.PreallocatedOrderId, request.SessionId);
            return Failure("orders.create.persist_failed", ex.Message);
        }

        // FR-014: convert inventory reservations. Runs AFTER order commit so a partial
        // conversion failure leaves us with order placed + fulfillment_state=awaiting_stock,
        // not a ghost order. Each reservation is converted independently — one bad apple
        // doesn't fail the rest.
        var convertedAll = await ConvertReservationsAsync(order, cancellationToken);
        if (!convertedAll)
        {
            order.FulfillmentState = FulfillmentSm.AwaitingStock;
            ordersDb.StateTransitions.Add(new OrderStateTransition
            {
                OrderId = order.Id,
                Machine = OrderStateTransition.MachineFulfillment,
                FromState = FulfillmentSm.NotStarted,
                ToState = FulfillmentSm.AwaitingStock,
                ActorAccountId = null,
                Trigger = "system.inventory_convert_partial",
                Reason = "one_or_more_reservations_failed_to_convert",
                OccurredAt = DateTimeOffset.UtcNow,
            });
            ordersDb.Outbox.Add(new OrdersOutboxEntry
            {
                EventType = "fulfillment.awaiting_stock",
                AggregateId = order.Id,
                PayloadJson = JsonSerializer.Serialize(new { orderId = order.Id, orderNumber = order.OrderNumber }),
                CommittedAt = DateTimeOffset.UtcNow,
            });
            try { await ordersDb.SaveChangesAsync(cancellationToken); }
            catch (Exception saveEx)
            {
                logger.LogError(saveEx,
                    "orders.create.awaiting_stock_persist_failed orderId={OrderId}", order.Id);
            }
        }

        logger.LogInformation(
            "orders.create.success orderId={OrderId} orderNumber={OrderNumber} paymentState={PaymentState}",
            order.Id, order.OrderNumber, order.PaymentState);
        return new OrderFromCheckoutResult(
            IsSuccess: true,
            OrderId: order.Id,
            OrderNumber: order.OrderNumber,
            PaymentState: MapPaymentStateToWire(order.PaymentState));
    }

    private async Task<bool> ConvertReservationsAsync(Order order, CancellationToken ct)
    {
        var allOk = true;
        foreach (var line in order.Lines)
        {
            if (line.ReservationId is not { } reservationId)
            {
                continue;
            }
            try
            {
                var convertResult = await BackendApi.Modules.Inventory.Internal.Reservations.Convert.Handler.HandleAsync(
                    reservationId,
                    new BackendApi.Modules.Inventory.Internal.Reservations.Convert.ConvertReservationRequest(
                        OrderId: order.Id,
                        AccountId: order.AccountId),
                    inventoryDb,
                    atsCalculator,
                    bucketMapper,
                    reorderAlertEmitter,
                    availabilityEventEmitter,
                    auditEventPublisher,
                    logger,
                    ct);
                if (!convertResult.IsSuccess)
                {
                    allOk = false;
                    logger.LogWarning(
                        "orders.create.reservation_convert_failed orderId={OrderId} reservationId={ReservationId} reason={Reason}",
                        order.Id, reservationId, convertResult.ReasonCode);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                allOk = false;
                logger.LogError(ex,
                    "orders.create.reservation_convert_threw orderId={OrderId} reservationId={ReservationId}",
                    order.Id, reservationId);
            }
        }
        return allOk;
    }

    /// <summary>Map our domain payment state to the contract's "captured" | "pending" | "pending_cod" wire vocabulary.</summary>
    private static string MapPaymentStateToWire(string domainState) => domainState switch
    {
        var s when string.Equals(s, PaymentSm.Captured, StringComparison.OrdinalIgnoreCase) => "captured",
        var s when string.Equals(s, PaymentSm.Authorized, StringComparison.OrdinalIgnoreCase) => "authorized",
        var s when string.Equals(s, PaymentSm.PendingBankTransfer, StringComparison.OrdinalIgnoreCase) => "pending",
        var s when string.Equals(s, PaymentSm.PendingCod, StringComparison.OrdinalIgnoreCase) => "pending_cod",
        _ => domainState,
    };

    /// <summary>
    /// Initial domain payment-state from the checkout-supplied method. For sync card payments
    /// we mirror spec 010's <c>StubOrderFromCheckoutHandler</c> which assumed synchronous
    /// capture. The webhook (Phase F1) advances Authorized→Captured for real authorize-only
    /// flows; PaymentSm self-transitions absorb duplicates.
    /// </summary>
    private static string ResolveInitialPaymentState(string paymentMethod)
    {
        if (string.Equals(paymentMethod, "bank_transfer", StringComparison.OrdinalIgnoreCase))
        {
            return PaymentSm.PendingBankTransfer;
        }
        if (string.Equals(paymentMethod, "cod", StringComparison.OrdinalIgnoreCase))
        {
            return PaymentSm.PendingCod;
        }
        return PaymentSm.Captured;
    }

    private static OrderFromCheckoutResult Failure(string code, string message) =>
        new(IsSuccess: false, OrderId: null, OrderNumber: null, PaymentState: null,
            ErrorCode: code, ErrorMessage: message);

    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505";
}
