using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Inventory.Primitives;
using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Orders.Internal.CreateFromQuotation;

public sealed record CreateFromQuotationResult(
    bool IsSuccess,
    Guid? OrderId,
    string? OrderNumber,
    string? ErrorCode,
    string? ErrorMessage);

/// <summary>
/// FR-011 / FR-012 / SC-006. Converts an active quotation into a placed order at the stored
/// price (no re-pricing — the explanation hash is reused byte-identically). Spec 011 ships
/// the handler; the customer-facing accept and admin convert endpoints route through here.
///
/// Inventory is NOT re-reserved: the quotation acceptance creates an order in
/// <c>fulfillment_state=not_started</c>; if stock is insufficient at fulfillment time the
/// order falls to <c>awaiting_stock</c> via the same path as
/// <see cref="BackendApi.Modules.Orders.Internal.CreateFromCheckout.CreateFromCheckoutHandler"/>
/// (spec edge case 8).
/// </summary>
public sealed class CreateFromQuotationHandler(
    OrdersDbContext db,
    OrderNumberSequencer sequencer,
    IAuditEventPublisher auditEventPublisher,
    ILogger<CreateFromQuotationHandler> logger)
{
    public async Task<CreateFromQuotationResult> CreateAsync(
        Guid quotationId,
        Guid? actorAccountId,
        CancellationToken ct)
    {
        var quotation = await db.Quotations.Include(q => q.Lines).FirstOrDefaultAsync(q => q.Id == quotationId, ct);
        if (quotation is null)
        {
            return new CreateFromQuotationResult(false, null, null, "order.quote.not_found", "Quotation not found.");
        }
        if (!string.Equals(quotation.Status, Quotation.StatusActive, StringComparison.OrdinalIgnoreCase))
        {
            return new CreateFromQuotationResult(false, null, null, "order.quote.invalid_status",
                $"Quotation status is '{quotation.Status}'; only 'active' quotes can be converted.");
        }
        if (quotation.ValidUntil <= DateTimeOffset.UtcNow)
        {
            return new CreateFromQuotationResult(false, null, null, "order.quote.expired", "Quotation has expired.");
        }
        if (quotation.ConvertedOrderId is not null)
        {
            // Idempotent — return existing.
            var existing = await db.Orders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == quotation.ConvertedOrderId, ct);
            return existing is not null
                ? new CreateFromQuotationResult(true, existing.Id, existing.OrderNumber, null, null)
                : new CreateFromQuotationResult(false, null, null, "order.quote.integrity_fail",
                    "Quotation references a missing order.");
        }
        if (quotation.Lines.Count == 0)
        {
            return new CreateFromQuotationResult(false, null, null, "order.quote.empty",
                "Quotation has no lines.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        // Pre-allocated order id — distinct from checkout's path, but kept consistent with
        // the catalog snapshot pattern.
        var orderId = Guid.NewGuid();

        // Compute totals from the quotation lines (already snapshotted at quote-time).
        var subtotal = quotation.Lines.Sum(l => (long)l.UnitPriceMinor * l.Qty);
        var lineTaxTotal = quotation.Lines.Sum(l => l.LineTaxMinor);
        var lineDiscountTotal = quotation.Lines.Sum(l => l.LineDiscountMinor);
        var grand = quotation.Lines.Sum(l => l.LineTotalMinor);

        var orderNumber = await sequencer.NextAsync(quotation.MarketCode, nowUtc, ct);

        var order = new Order
        {
            Id = orderId,
            OrderNumber = orderNumber,
            AccountId = quotation.AccountId,
            MarketCode = quotation.MarketCode,
            // B1 fix: was hardcoded "SAR" — broke EG quotations. MarketCurrency is the temporary
            // single source of truth until a market-config service ships. Pricing's explanation
            // row IS the canonical currency for checkout-originated orders; quotations don't
            // re-read it, so this map covers them.
            Currency = MarketCurrency.Resolve(quotation.MarketCode),
            SubtotalMinor = subtotal,
            DiscountMinor = lineDiscountTotal,
            TaxMinor = lineTaxTotal,
            ShippingMinor = 0,
            GrandTotalMinor = grand,
            PriceExplanationId = quotation.PriceExplanationId,
            ShippingAddressJson = "{}",
            BillingAddressJson = "{}",
            OrderState = OrderSm.Placed,
            // Quotation-originated orders default to bank transfer pending — admin confirms
            // via E8 ConfirmBankTransfer; B2B norm.
            PaymentState = PaymentSm.PendingBankTransfer,
            FulfillmentState = FulfillmentSm.NotStarted,
            RefundState = RefundSm.None,
            PlacedAt = nowUtc,
            QuotationId = quotation.Id,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
        foreach (var ql in quotation.Lines)
        {
            order.Lines.Add(new OrderLine
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ProductId = ql.ProductId,
                Sku = ql.Sku,
                NameAr = ql.NameAr,
                NameEn = ql.NameEn,
                Qty = ql.Qty,
                UnitPriceMinor = ql.UnitPriceMinor,
                LineDiscountMinor = ql.LineDiscountMinor,
                LineTaxMinor = ql.LineTaxMinor,
                LineTotalMinor = ql.LineTotalMinor,
                Restricted = ql.Restricted,
                AttributesJson = ql.AttributesJson,
            });
        }
        db.Orders.Add(order);

        quotation.Status = Quotation.StatusConverted;
        quotation.ConvertedOrderId = order.Id;
        quotation.UpdatedAt = nowUtc;

        db.StateTransitions.Add(new OrderStateTransition
        {
            OrderId = order.Id,
            Machine = OrderStateTransition.MachineOrder,
            FromState = string.Empty,
            ToState = OrderSm.Placed,
            ActorAccountId = actorAccountId,
            Trigger = "quotation.convert",
            Reason = $"quotationId={quotation.Id}",
            OccurredAt = nowUtc,
        });
        db.StateTransitions.Add(new OrderStateTransition
        {
            OrderId = order.Id,
            Machine = OrderStateTransition.MachinePayment,
            FromState = string.Empty,
            ToState = order.PaymentState,
            ActorAccountId = actorAccountId,
            Trigger = "quotation.convert",
            Reason = "default_pending_bank_transfer",
            OccurredAt = nowUtc,
        });
        db.Outbox.Add(new OrdersOutboxEntry
        {
            EventType = "order.placed",
            AggregateId = order.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                orderId = order.Id,
                orderNumber = order.OrderNumber,
                source = "quotation",
                quotationId = quotation.Id,
                grandTotalMinor = order.GrandTotalMinor,
                currency = order.Currency,
            }),
            CommittedAt = nowUtc,
        });
        db.Outbox.Add(new OrdersOutboxEntry
        {
            EventType = "quote.converted",
            AggregateId = quotation.Id,
            PayloadJson = JsonSerializer.Serialize(new { quotationId = quotation.Id, orderId = order.Id }),
            CommittedAt = nowUtc,
        });

        await db.SaveChangesAsync(ct);

        try
        {
            await auditEventPublisher.PublishAsync(new AuditEvent(
                ActorId: actorAccountId ?? Guid.Empty,
                ActorRole: "system",
                Action: "orders.quotation.converted",
                EntityType: "orders.quotation",
                EntityId: quotation.Id,
                BeforeState: new { status = Quotation.StatusActive },
                AfterState: new { status = Quotation.StatusConverted, orderId = order.Id },
                Reason: null), ct);
        }
        catch { /* audit best-effort — local transition rows are canonical */ }

        logger.LogInformation(
            "orders.create_from_quotation.success quotationId={QuotationId} orderId={OrderId} orderNumber={OrderNumber}",
            quotation.Id, order.Id, order.OrderNumber);
        return new CreateFromQuotationResult(true, order.Id, order.OrderNumber, null, null);
    }
}
