using BackendApi.Modules.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BackendApi.Modules.B2B.Documents.PdfTemplates;

/// <summary>
/// Spec 021 T089 (EN) / T090 (AR) — QuestPDF template for a published
/// <see cref="Entities.QuoteVersion"/>. Single template handles both locales —
/// the locale parameter switches the title, header copy, RTL alignment, and font.
///
/// Layout per data-model §2.7 + research §R3:
/// <list type="bullet">
///   <item>Header: title + market code + version number.</item>
///   <item>Customer + company block with PO and validity.</item>
///   <item>Line items table (SKU, qty, unit price, discount, tax preview, total).</item>
///   <item>Totals block (subtotal, discount, tax preview, grand total).</item>
///   <item>Terms text (locale-specific) + Net-X days.</item>
/// </list>
///
/// Tax presented here is a PREVIEW (FR-038). Authoritative tax is computed at
/// order conversion (spec 011); the customer PDF reproduces the preview so the
/// customer sees what was quoted.
/// </summary>
public sealed class QuoteVersionPdfTemplate : IDocument
{
    public const string TemplateName = "quote-version";

    private readonly LocaleCode _locale;
    private readonly QuoteVersionPdfData _data;

    public QuoteVersionPdfTemplate(LocaleCode locale, object data)
    {
        _locale = locale;
        _data = data as QuoteVersionPdfData
            ?? throw new ArgumentException(
                $"Expected {nameof(QuoteVersionPdfData)} payload for the quote-version template.",
                nameof(data));
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        var ar = _locale == LocaleCode.AR;
        var title = ar ? "عرض سعر" : "Quotation";
        var versionLabel = ar ? "النسخة" : "Version";
        var poLabel = ar ? "أمر الشراء" : "PO";
        var validUntilLabel = ar ? "صالح حتى" : "Valid until";
        var customerLabel = ar ? "العميل" : "Customer";
        var companyLabel = ar ? "الشركة" : "Company";
        var skuLabel = ar ? "الصنف" : "SKU";
        var qtyLabel = ar ? "الكمية" : "Qty";
        var unitPriceLabel = ar ? "سعر الوحدة" : "Unit Price";
        var discountLabel = ar ? "الخصم" : "Discount";
        var taxLabel = ar ? "الضريبة (تقدير)" : "Tax (preview)";
        var totalLabel = ar ? "الإجمالي" : "Total";
        var subtotalLabel = ar ? "المجموع الفرعي" : "Subtotal";
        var grandTotalLabel = ar ? "الإجمالي الكلي" : "Grand Total";
        var termsLabel = ar ? "الشروط" : "Terms";
        var netDaysLabel = ar ? "صافي خلال" : "Net";
        var daysLabel = ar ? "أيام" : "days";
        var marketLabel = ar ? "السوق" : "Market";

        var fontFamily = ar ? "NotoNaskhArabic" : Fonts.Arial;

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(30);
            page.DefaultTextStyle(style => style.FontFamily(fontFamily).FontSize(11));

            page.Header().Element(c =>
            {
                c.Column(col =>
                {
                    col.Item().AlignedHeader(ar).Text(title).FontSize(22).SemiBold();
                    col.Item().AlignedHeader(ar).Text($"{versionLabel}: {_data.VersionNumber}").FontSize(10);
                    col.Item().AlignedHeader(ar).Text($"{marketLabel}: {_data.MarketCode.ToUpperInvariant()}").FontSize(10);
                });
            });

            page.Content().Column(col =>
            {
                col.Spacing(10);

                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text($"{customerLabel}: {_data.CustomerName}").SemiBold();
                        c.Item().Text($"{companyLabel}: {_data.CompanyName}");
                    });
                    row.RelativeItem().Column(c =>
                    {
                        if (!string.IsNullOrWhiteSpace(_data.PoNumber))
                        {
                            c.Item().Text($"{poLabel}: {_data.PoNumber}");
                        }
                        if (_data.ExpiresAt is { } expiresAt)
                        {
                            c.Item().Text($"{validUntilLabel}: {expiresAt:yyyy-MM-dd}");
                        }
                    });
                });

                col.Item().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(cd =>
                    {
                        cd.RelativeColumn(3);
                        cd.RelativeColumn(1);
                        cd.RelativeColumn(2);
                        cd.RelativeColumn(2);
                        cd.RelativeColumn(2);
                        cd.RelativeColumn(2);
                    });

                    table.Header(h =>
                    {
                        h.Cell().Text(skuLabel).SemiBold();
                        h.Cell().Text(qtyLabel).SemiBold();
                        h.Cell().Text(unitPriceLabel).SemiBold();
                        h.Cell().Text(discountLabel).SemiBold();
                        h.Cell().Text(taxLabel).SemiBold();
                        h.Cell().Text(totalLabel).SemiBold();
                    });

                    foreach (var line in _data.Lines)
                    {
                        table.Cell().Text(line.Sku);
                        table.Cell().Text(line.Quantity.ToString());
                        table.Cell().Text($"{line.UnitPrice:N2} {_data.Currency}");
                        table.Cell().Text($"{line.LineDiscountAmount:N2}");
                        table.Cell().Text($"{line.LineTaxPreview:N2}");
                        table.Cell().Text($"{line.LineTotal:N2}");
                    }
                });

                col.Item().PaddingTop(12).Row(row =>
                {
                    row.RelativeItem();
                    row.ConstantItem(220).Column(t =>
                    {
                        t.Item().Text($"{subtotalLabel}: {_data.Subtotal:N2} {_data.Currency}");
                        t.Item().Text($"{discountLabel}: {_data.TotalDiscount:N2}");
                        t.Item().Text($"{taxLabel}: {_data.TotalTaxPreview:N2}");
                        t.Item().Text($"{grandTotalLabel}: {_data.GrandTotal:N2} {_data.Currency}").SemiBold();
                    });
                });

                col.Item().PaddingTop(16).Column(t =>
                {
                    t.Item().Text(termsLabel).SemiBold();
                    var termsText = ar ? _data.TermsTextAr : _data.TermsTextEn;
                    if (!string.IsNullOrWhiteSpace(termsText))
                    {
                        t.Item().Text(termsText).FontSize(10);
                    }
                    if (_data.TermsDays > 0)
                    {
                        t.Item().Text($"{netDaysLabel} {_data.TermsDays} {daysLabel}").FontSize(10);
                    }
                });
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.Span(ar ? "تم الإنشاء بواسطة BuidSass" : "Generated by BuidSass").FontSize(9);
                text.Span($"  •  {_data.PublishedAt:yyyy-MM-dd HH:mm}Z").FontSize(9);
            });
        });
    }
}

internal static class QuoteVersionPdfTemplateContainerExtensions
{
    public static IContainer AlignedHeader(this IContainer container, bool isArabic) =>
        isArabic ? container.AlignRight() : container.AlignLeft();
}
