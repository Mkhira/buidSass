namespace BackendApi.Modules.B2B.Documents.PdfTemplates;

/// <summary>
/// Spec 021 T089/T090 — locale-agnostic data envelope for the quote-version PDF
/// templates. Both EN and AR templates consume this same shape; locale-switching
/// happens in the template's <c>Compose</c> method.
/// </summary>
public sealed record QuoteVersionPdfData(
    Guid QuoteId,
    int VersionNumber,
    string MarketCode,
    string CompanyName,
    string CustomerName,
    string? PoNumber,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ExpiresAt,
    string TermsTextEn,
    string TermsTextAr,
    int TermsDays,
    IReadOnlyList<QuoteVersionPdfLine> Lines,
    decimal Subtotal,
    decimal TotalDiscount,
    decimal TotalTaxPreview,
    decimal GrandTotal,
    string Currency);

public sealed record QuoteVersionPdfLine(
    string Sku,
    int Quantity,
    decimal UnitPrice,
    decimal LineDiscountAmount,
    decimal LineTaxPreview,
    decimal LineTotal);
