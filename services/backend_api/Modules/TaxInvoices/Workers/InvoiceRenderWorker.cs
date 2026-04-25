using System.Security.Cryptography;
using System.Text.Json;
using BackendApi.Modules.TaxInvoices.Entities;
using BackendApi.Modules.TaxInvoices.Persistence;
using BackendApi.Modules.TaxInvoices.Primitives;
using BackendApi.Modules.TaxInvoices.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.TaxInvoices.Workers;

/// <summary>
/// FR-013 + research R9 — async PDF render queue. Claims jobs via
/// <c>FOR UPDATE SKIP LOCKED</c>, renders + uploads, marks <c>done</c> / <c>failed</c>.
/// Exponential backoff up to <see cref="InvoiceRenderJob.MaxAttempts"/>.
/// </summary>
public sealed class InvoiceRenderWorker(
    IServiceProvider services,
    ILogger<InvoiceRenderWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private const int BatchSize = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("invoices.render_worker.started interval={Interval}s batch={Batch}",
            PollInterval.TotalSeconds, BatchSize);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await DrainBatchAsync(stoppingToken);
                if (processed == 0)
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "invoices.render_worker.cycle_failed");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    private async Task<int> DrainBatchAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;

        // B4 fix — claim + state-flip MUST be one transaction. Without the explicit tx the
        // FOR UPDATE SKIP LOCKED row lock releases at the end of the SELECT, so a second
        // worker could theoretically grab the same row before our state-flip commits. The
        // state-IN filter ('queued','failed') makes a double-claim benign in practice (the
        // second worker's flip would still see 'queued' until our SaveChanges lands), but
        // wrapping in BeginTransactionAsync makes the contract explicit + audit-friendly.
        await using var claimTx = await db.Database.BeginTransactionAsync(ct);
        var jobs = await db.RenderJobs
            .FromSqlInterpolated($"""
                SELECT * FROM invoices.invoice_render_jobs
                WHERE "State" IN ('queued','failed')
                  AND "NextAttemptAt" <= {nowUtc}
                  AND "Attempts" < {InvoiceRenderJob.MaxAttempts}
                ORDER BY "NextAttemptAt"
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);
        if (jobs.Count == 0)
        {
            await claimTx.RollbackAsync(ct);
            return 0;
        }
        foreach (var job in jobs)
        {
            job.State = InvoiceRenderJob.StateRendering;
            job.Attempts += 1;
        }
        await db.SaveChangesAsync(ct);
        await claimTx.CommitAsync(ct);

        var renderer = scope.ServiceProvider.GetRequiredService<HtmlTemplateRenderer>();
        var pdfExporter = scope.ServiceProvider.GetRequiredService<PdfExporter>();
        var blobStore = scope.ServiceProvider.GetRequiredService<IInvoiceBlobStore>();
        var templates = scope.ServiceProvider.GetRequiredService<InvoiceTemplateResolver>();

        foreach (var job in jobs)
        {
            try
            {
                if (job.InvoiceId is { } invoiceId)
                {
                    await RenderInvoiceAsync(db, invoiceId, renderer, pdfExporter, blobStore, templates, ct);
                }
                else if (job.CreditNoteId is { } creditNoteId)
                {
                    await RenderCreditNoteAsync(db, creditNoteId, renderer, pdfExporter, blobStore, templates, ct);
                }
                else
                {
                    throw new InvalidOperationException("Render job has neither invoice nor credit-note id.");
                }
                job.State = InvoiceRenderJob.StateDone;
                job.LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "invoices.render.failed jobId={JobId} attempts={Attempts}", job.Id, job.Attempts);
                job.LastError = ex.GetType().Name + ": " + ex.Message;
                if (job.Attempts >= InvoiceRenderJob.MaxAttempts)
                {
                    job.State = InvoiceRenderJob.StateFailed;
                    job.NextAttemptAt = nowUtc;
                    if (job.InvoiceId is { } invId)
                    {
                        await MarkInvoiceFailedAsync(db, invId, ex.Message, ct);
                    }
                    else if (job.CreditNoteId is { } cnId)
                    {
                        await MarkCreditNoteFailedAsync(db, cnId, ex.Message, ct);
                    }
                }
                else
                {
                    job.State = InvoiceRenderJob.StateFailed;
                    job.NextAttemptAt = nowUtc.AddSeconds(Math.Pow(2, job.Attempts) * 5);
                }
            }
        }
        await db.SaveChangesAsync(ct);
        return jobs.Count;
    }

    private static async Task RenderInvoiceAsync(
        InvoicesDbContext db,
        Guid invoiceId,
        HtmlTemplateRenderer renderer,
        PdfExporter pdfExporter,
        IInvoiceBlobStore blobStore,
        InvoiceTemplateResolver templates,
        CancellationToken ct)
    {
        var invoice = await db.Invoices.Include(i => i.Lines).SingleAsync(i => i.Id == invoiceId, ct);
        var template = await templates.LoadAsync(invoice.MarketCode, ct);
        var model = ComposeModel(invoice, template);
        // HTML composition is exercised so renderer regressions surface even if the spec 003
        // PDF layer ignores its input today.
        _ = renderer.Compose(model);
        var pdfBytes = await pdfExporter.ExportAsync(model, ct);
        var sha = Convert.ToHexString(SHA256.HashData(pdfBytes)).ToLowerInvariant();
        var key = blobStore.ResolveInvoiceKey(invoice.MarketCode, invoice.IssuedAt, invoice.InvoiceNumber);
        await blobStore.PutAsync(key, pdfBytes, "application/pdf", ct);
        invoice.PdfBlobKey = key;
        invoice.PdfSha256 = sha;
        invoice.State = Invoice.StateRendered;
        invoice.RenderAttempts += 1;
        invoice.LastError = null;
        invoice.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static async Task RenderCreditNoteAsync(
        InvoicesDbContext db,
        Guid creditNoteId,
        HtmlTemplateRenderer renderer,
        PdfExporter pdfExporter,
        IInvoiceBlobStore blobStore,
        InvoiceTemplateResolver templates,
        CancellationToken ct)
    {
        var creditNote = await db.CreditNotes.Include(c => c.Lines)
            .SingleAsync(c => c.Id == creditNoteId, ct);
        var invoice = await db.Invoices.Include(i => i.Lines)
            .SingleAsync(i => i.Id == creditNote.InvoiceId, ct);
        var template = await templates.LoadAsync(invoice.MarketCode, ct);
        var model = ComposeCreditNoteModel(creditNote, invoice, template);
        _ = renderer.Compose(model);
        var pdfBytes = await pdfExporter.ExportAsync(model, ct);
        var sha = Convert.ToHexString(SHA256.HashData(pdfBytes)).ToLowerInvariant();
        var key = blobStore.ResolveCreditNoteKey(invoice.MarketCode, creditNote.IssuedAt, creditNote.CreditNoteNumber);
        await blobStore.PutAsync(key, pdfBytes, "application/pdf", ct);
        creditNote.PdfBlobKey = key;
        creditNote.PdfSha256 = sha;
        creditNote.State = CreditNote.StateRendered;
        creditNote.RenderAttempts += 1;
        creditNote.LastError = null;
        creditNote.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static InvoiceRenderModel ComposeModel(Invoice invoice, InvoiceTemplate template)
    {
        var bill = ParseBlock(invoice.BillToJson);
        var bank = ParseBlock(template.BankDetailsJson);
        return new InvoiceRenderModel(
            InvoiceNumber: invoice.InvoiceNumber,
            OrderNumber: bill.GetValueOrDefault("orderNumber") as string ?? string.Empty,
            MarketCode: invoice.MarketCode,
            Currency: invoice.Currency,
            IssuedAt: invoice.IssuedAt,
            SellerLegalNameAr: template.SellerLegalNameAr,
            SellerLegalNameEn: template.SellerLegalNameEn,
            SellerVatNumber: template.SellerVatNumber,
            SellerAddressAr: template.SellerAddressAr,
            SellerAddressEn: template.SellerAddressEn,
            BillToAr: bill.GetValueOrDefault("name") as string ?? string.Empty,
            BillToEn: bill.GetValueOrDefault("name") as string ?? string.Empty,
            B2bPoNumber: invoice.B2bPoNumber,
            BuyerVatNumber: bill.GetValueOrDefault("buyerVatNumber") as string,
            SubtotalMinor: invoice.SubtotalMinor,
            DiscountMinor: invoice.DiscountMinor,
            TaxMinor: invoice.TaxMinor,
            ShippingMinor: invoice.ShippingMinor,
            GrandTotalMinor: invoice.GrandTotalMinor,
            FooterHtmlAr: template.FooterHtmlAr,
            FooterHtmlEn: template.FooterHtmlEn,
            BankNameAr: bank.GetValueOrDefault("bankNameAr") as string,
            BankNameEn: bank.GetValueOrDefault("bankNameEn") as string,
            Iban: bank.GetValueOrDefault("iban") as string,
            ZatcaQrB64: invoice.ZatcaQrB64,
            Lines: invoice.Lines.Select((l, idx) => new InvoiceRenderLine(
                Number: idx + 1,
                Sku: l.Sku, NameAr: l.NameAr, NameEn: l.NameEn, Qty: l.Qty,
                UnitPriceMinor: l.UnitPriceMinor, LineDiscountMinor: l.LineDiscountMinor,
                LineTaxMinor: l.LineTaxMinor, LineTotalMinor: l.LineTotalMinor, TaxRateBp: l.TaxRateBp))
                .ToList());
    }

    private static InvoiceRenderModel ComposeCreditNoteModel(CreditNote cn, Invoice originalInvoice, InvoiceTemplate template)
    {
        var bill = ParseBlock(originalInvoice.BillToJson);
        var bank = ParseBlock(template.BankDetailsJson);
        return new InvoiceRenderModel(
            InvoiceNumber: cn.CreditNoteNumber,
            OrderNumber: string.Empty,
            MarketCode: originalInvoice.MarketCode,
            Currency: originalInvoice.Currency,
            IssuedAt: cn.IssuedAt,
            SellerLegalNameAr: template.SellerLegalNameAr,
            SellerLegalNameEn: template.SellerLegalNameEn,
            SellerVatNumber: template.SellerVatNumber,
            SellerAddressAr: template.SellerAddressAr,
            SellerAddressEn: template.SellerAddressEn,
            BillToAr: bill.GetValueOrDefault("name") as string ?? string.Empty,
            BillToEn: bill.GetValueOrDefault("name") as string ?? string.Empty,
            B2bPoNumber: originalInvoice.B2bPoNumber,
            BuyerVatNumber: bill.GetValueOrDefault("buyerVatNumber") as string,
            SubtotalMinor: cn.SubtotalMinor,
            DiscountMinor: cn.DiscountMinor,
            TaxMinor: cn.TaxMinor,
            ShippingMinor: cn.ShippingMinor,
            GrandTotalMinor: cn.GrandTotalMinor,
            FooterHtmlAr: template.FooterHtmlAr,
            FooterHtmlEn: template.FooterHtmlEn,
            BankNameAr: bank.GetValueOrDefault("bankNameAr") as string,
            BankNameEn: bank.GetValueOrDefault("bankNameEn") as string,
            Iban: bank.GetValueOrDefault("iban") as string,
            ZatcaQrB64: cn.ZatcaQrB64,
            Lines: cn.Lines.Select((l, idx) => new InvoiceRenderLine(
                Number: idx + 1, Sku: l.Sku, NameAr: l.NameAr, NameEn: l.NameEn, Qty: l.Qty,
                UnitPriceMinor: l.UnitPriceMinor, LineDiscountMinor: l.LineDiscountMinor,
                LineTaxMinor: l.LineTaxMinor, LineTotalMinor: l.LineTotalMinor, TaxRateBp: l.TaxRateBp))
                .ToList(),
            IsCreditNote: true,
            CreditNoteOriginalInvoiceNumber: originalInvoice.InvoiceNumber);
    }

    private static Dictionary<string, object?> ParseBlock(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.TryGetInt64(out var n) ? (object)n : prop.Value.GetDouble(),
                    JsonValueKind.True or JsonValueKind.False => prop.Value.GetBoolean(),
                    _ => prop.Value.GetRawText(),
                };
            }
            return dict;
        }
        catch (JsonException) { return new(); }
    }

    private static async Task MarkInvoiceFailedAsync(InvoicesDbContext db, Guid invoiceId, string err, CancellationToken ct)
    {
        var invoice = await db.Invoices.SingleAsync(i => i.Id == invoiceId, ct);
        invoice.State = Invoice.StateFailed;
        invoice.LastError = err;
        invoice.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static async Task MarkCreditNoteFailedAsync(InvoicesDbContext db, Guid creditNoteId, string err, CancellationToken ct)
    {
        var cn = await db.CreditNotes.SingleAsync(c => c.Id == creditNoteId, ct);
        cn.State = CreditNote.StateFailed;
        cn.LastError = err;
        cn.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
