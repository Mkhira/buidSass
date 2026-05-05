using BackendApi.Modules.B2B.Documents.PdfTemplates;
using BackendApi.Modules.Pdf;
using BackendApi.Modules.Storage;

namespace BackendApi.Modules.B2B.Documents;

/// <summary>
/// Spec 021 T091 — synchronously renders a <see cref="PdfTemplates.QuoteVersionPdfData"/>
/// snapshot into an EN or AR PDF and uploads it via <see cref="IStorageService"/>.
/// Used by the Publish handler (T088) to materialize the two PDFs that pin a
/// <see cref="Entities.QuoteVersion"/>.
///
/// Returns the storage <c>fileId</c> (Guid token used by
/// <see cref="IStorageService.GetSignedUrlAsync"/>) — the Publish handler stores
/// it as the <c>QuoteVersionDocument.StorageKey</c>.
/// </summary>
public sealed class QuoteVersionPdfRenderer
{
    private readonly IPdfService _pdf;
    private readonly IStorageService _storage;

    public QuoteVersionPdfRenderer(IPdfService pdf, IStorageService storage)
    {
        _pdf = pdf;
        _storage = storage;
    }

    public async Task<string> RenderAndUploadAsync(
        QuoteVersionPdfData data,
        LocaleCode locale,
        CancellationToken ct)
    {
        var bytes = await _pdf.RenderAsync(QuoteVersionPdfTemplate.TemplateName, locale, data, ct);
        await using var stream = new MemoryStream(bytes, writable: false);
        var market = string.Equals(data.MarketCode, "eg", StringComparison.OrdinalIgnoreCase)
            ? MarketCode.EG
            : MarketCode.KSA;
        var fileName = $"quote-{data.QuoteId:N}-v{data.VersionNumber}-{(locale == LocaleCode.AR ? "ar" : "en")}.pdf";
        var stored = await _storage.UploadAsync(stream, fileName, "application/pdf", market, ct);
        return stored.FileId.ToString();
    }
}
