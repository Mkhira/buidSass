using System.Text.Json;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Observability;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.TaxInvoices.Entities;
using BackendApi.Modules.TaxInvoices.Persistence;
using BackendApi.Modules.TaxInvoices.Primitives;
using BackendApi.Modules.TaxInvoices.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.TaxInvoices.Internal.IssueOnCapture;

/// <summary>
/// FR-001 / FR-005 — issues a tax invoice for a captured order. Reads order + lines from
/// spec 011's tables (cross-schema read inside the monolith per research R12), snapshots
/// every billable field onto <see cref="Invoice"/> + <see cref="InvoiceLine"/>, generates
/// the invoice number + ZATCA QR, queues a render job, and emits <c>invoice.issued</c>.
/// Idempotent on <c>orderId</c>.
/// </summary>
public sealed record IssueOnCaptureResult(bool IsSuccess, Guid? InvoiceId, string? InvoiceNumber, string? ErrorCode, string? Detail);

public sealed class IssueOnCaptureHandler(
    InvoicesDbContext invoicesDb,
    OrdersDbContext ordersDb,
    IdentityDbContext identityDb,
    InvoiceNumberSequencer sequencer,
    InvoiceTemplateResolver templateResolver,
    ZatcaQrEmbedder qrEmbedder,
    InvoicesMetrics metrics,
    ILogger<IssueOnCaptureHandler> logger)
{
    public async Task<IssueOnCaptureResult> IssueAsync(Guid orderId, CancellationToken ct)
    {
        // I — explicit span links the orders.outbox dispatch to invoices.issued.
        using var activity = InvoicesTracing.Source.StartActivity("invoices.issue_on_capture");
        activity?.SetTag("invoices.order_id", orderId);
        if (orderId == Guid.Empty)
        {
            return new IssueOnCaptureResult(false, null, null, "invoice.invalid_order_id", "orderId is required.");
        }

        // Idempotency: one invoice per order.
        var existing = await invoicesDb.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.OrderId == orderId, ct);
        if (existing is not null)
        {
            logger.LogInformation(
                "invoices.issue_on_capture.idempotent_hit orderId={OrderId} invoiceNumber={Number}",
                orderId, existing.InvoiceNumber);
            return new IssueOnCaptureResult(true, existing.Id, existing.InvoiceNumber, null, null);
        }

        var order = await ordersDb.Orders.AsNoTracking()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null)
        {
            return new IssueOnCaptureResult(false, null, null, "invoice.order_not_found", "Order not found.");
        }
        // Phase 1B issuance contract — payment must be captured (or further-along refund states).
        if (!IsPaymentBookable(order.PaymentState))
        {
            return new IssueOnCaptureResult(false, null, null, "invoice.payment_not_captured",
                $"Order payment_state '{order.PaymentState}' is not yet eligible for invoice issuance.");
        }
        if (order.Lines.Count == 0)
        {
            return new IssueOnCaptureResult(false, null, null, "invoice.no_lines", "Order has no lines.");
        }

        InvoiceTemplate template;
        try
        {
            template = await templateResolver.LoadAsync(order.MarketCode, ct);
        }
        catch (InvalidOperationException ex)
        {
            return new IssueOnCaptureResult(false, null, null, "invoice.template.missing", ex.Message);
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var invoiceNumber = await sequencer.NextAsync(order.MarketCode, nowUtc, ct);

        // Snapshot the bill-to party. Customer (B2C) takes the order's account email/display
        // name; B2B billing rolls in via the order's billing-address blob — full B2B
        // account-type wiring is a Phase 1.5 follow-up.
        var account = await identityDb.Accounts.AsNoTracking()
            .Where(a => a.Id == order.AccountId)
            .Select(a => new { a.DisplayName, a.EmailDisplay })
            .FirstOrDefaultAsync(ct);
        // CR Major fix — store the fields the renderer reads at top level. Earlier shape
        // nested billing under `address` and named the PO `poNumber`; the render model expects
        // top-level `orderNumber` + `buyerVatNumber`. The renderer's reader is permissive (it
        // ignores keys it doesn't know), so emitting both shapes here is forward-compatible.
        var billToBlock = JsonSerializer.Serialize(new
        {
            name = account?.DisplayName ?? "Customer",
            email = account?.EmailDisplay,
            orderNumber = order.OrderNumber,
            poNumber = order.B2bPoNumber,
            buyerVatNumber = (string?)null, // populated when spec 004 surfaces business profile
            address = TryParseJson(order.BillingAddressJson),
        });
        var sellerBlock = JsonSerializer.Serialize(new
        {
            legalNameAr = template.SellerLegalNameAr,
            legalNameEn = template.SellerLegalNameEn,
            vatNumber = template.SellerVatNumber,
            addressAr = template.SellerAddressAr,
            addressEn = template.SellerAddressEn,
        });

        var market = order.MarketCode;
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = invoiceNumber,
            OrderId = order.Id,
            AccountId = order.AccountId,
            MarketCode = market,
            Currency = order.Currency,
            IssuedAt = nowUtc,
            PriceExplanationId = order.PriceExplanationId,
            SubtotalMinor = order.SubtotalMinor,
            DiscountMinor = order.DiscountMinor,
            TaxMinor = order.TaxMinor,
            ShippingMinor = order.ShippingMinor,
            GrandTotalMinor = order.GrandTotalMinor,
            BillToJson = billToBlock,
            SellerJson = sellerBlock,
            B2bPoNumber = order.B2bPoNumber,
            State = Invoice.StatePending,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
        // Tax-rate basis points are derived from the line's tax / pre-tax totals; if the line
        // has no tax (e.g. zero-rated B2B), TaxRateBp = 0.
        foreach (var ol in order.Lines)
        {
            var preTax = (long)ol.UnitPriceMinor * ol.Qty - ol.LineDiscountMinor;
            int taxRateBp = preTax > 0 ? (int)Math.Round(ol.LineTaxMinor * 10000m / preTax) : 0;
            invoice.Lines.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoice.Id,
                OrderLineId = ol.Id,
                MarketCode = market,
                Sku = ol.Sku,
                NameAr = ol.NameAr,
                NameEn = ol.NameEn,
                Qty = ol.Qty,
                UnitPriceMinor = ol.UnitPriceMinor,
                LineDiscountMinor = ol.LineDiscountMinor,
                LineTaxMinor = ol.LineTaxMinor,
                LineTotalMinor = ol.LineTotalMinor,
                TaxRateBp = taxRateBp,
            });
        }
        invoice.ZatcaQrB64 = qrEmbedder.BuildIfApplicable(order.MarketCode, template, invoice);

        invoicesDb.Invoices.Add(invoice);
        invoicesDb.RenderJobs.Add(new InvoiceRenderJob
        {
            InvoiceId = invoice.Id,
            MarketCode = market,
            State = InvoiceRenderJob.StateQueued,
            Attempts = 0,
            NextAttemptAt = nowUtc,
            CreatedAt = nowUtc,
        });
        invoicesDb.Outbox.Add(new InvoicesOutboxEntry
        {
            EventType = "invoice.issued",
            AggregateId = invoice.Id,
            MarketCode = market,
            PayloadJson = JsonSerializer.Serialize(new
            {
                invoiceId = invoice.Id,
                invoiceNumber = invoice.InvoiceNumber,
                orderId = order.Id,
                orderNumber = order.OrderNumber,
                accountId = order.AccountId,
                market = order.MarketCode,
                grandTotalMinor = invoice.GrandTotalMinor,
                currency = invoice.Currency,
            }),
            CommittedAt = nowUtc,
        });

        try
        {
            await invoicesDb.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505")
        {
            // Concurrent issuance lost the race. Re-read.
            var raced = await invoicesDb.Invoices.AsNoTracking()
                .FirstOrDefaultAsync(i => i.OrderId == orderId, ct);
            if (raced is not null)
            {
                return new IssueOnCaptureResult(true, raced.Id, raced.InvoiceNumber, null, null);
            }
            return new IssueOnCaptureResult(false, null, null, "invoice.duplicate_key", ex.Message);
        }

        activity?.SetTag("invoices.invoice_number", invoiceNumber);
        activity?.SetTag("invoices.market", order.MarketCode);
        metrics.IncrementIssued(order.MarketCode);
        logger.LogInformation(
            "invoices.issue_on_capture.success orderId={OrderId} invoiceNumber={Number} market={Market}",
            order.Id, invoiceNumber, order.MarketCode);
        return new IssueOnCaptureResult(true, invoice.Id, invoice.InvoiceNumber, null, null);
    }

    private static bool IsPaymentBookable(string paymentState) =>
        string.Equals(paymentState, "captured", StringComparison.OrdinalIgnoreCase)
        || string.Equals(paymentState, "partially_refunded", StringComparison.OrdinalIgnoreCase)
        || string.Equals(paymentState, "refunded", StringComparison.OrdinalIgnoreCase);

    private static object? TryParseJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { using var doc = JsonDocument.Parse(raw); return JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText()); }
        catch (JsonException) { return null; }
    }
}
