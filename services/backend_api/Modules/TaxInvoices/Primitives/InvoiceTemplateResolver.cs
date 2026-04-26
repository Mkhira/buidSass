using BackendApi.Modules.TaxInvoices.Entities;
using BackendApi.Modules.TaxInvoices.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.TaxInvoices.Primitives;

/// <summary>
/// Loads the per-market <see cref="InvoiceTemplate"/> row (FR-017): seller legal entity, VAT
/// number, address, footer HTML, bank details. The Razor templates themselves are compiled
/// at build time (research R6); this resolver only feeds them runtime variables.
///
/// Misses on the lookup are surfaced as <c>invoice.template.missing</c> rather than silently
/// rendering with empty fields — an unconfigured market should block issuance loudly so
/// finance/legal can fill the row before the next capture lands.
/// </summary>
public sealed class InvoiceTemplateResolver(InvoicesDbContext db)
{
    public async Task<InvoiceTemplate> LoadAsync(string marketCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(marketCode))
        {
            throw new ArgumentException("Market code is required.", nameof(marketCode));
        }
        var key = marketCode.Trim();
        var template = await db.InvoiceTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.MarketCode == key, ct);
        if (template is null)
        {
            throw new InvalidOperationException(
                $"invoice.template.missing — no invoice template configured for market '{key}'.");
        }
        return template;
    }
}
