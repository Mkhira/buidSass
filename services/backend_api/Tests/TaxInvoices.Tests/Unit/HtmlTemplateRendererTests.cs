using BackendApi.Modules.TaxInvoices.Rendering;
using FluentAssertions;

namespace TaxInvoices.Tests.Unit;

public sealed class HtmlTemplateRendererTests
{
    private static InvoiceRenderModel SampleModel(bool isCreditNote = false) => new(
        InvoiceNumber: "INV-KSA-202604-000187",
        OrderNumber: "ORD-KSA-202604-000001",
        MarketCode: "KSA",
        Currency: "SAR",
        IssuedAt: new DateTimeOffset(2026, 4, 15, 10, 30, 0, TimeSpan.Zero),
        SellerLegalNameAr: "منصة تجارة الأسنان المحدودة",
        SellerLegalNameEn: "Dental Commerce LLC",
        SellerVatNumber: "300000000000003",
        SellerAddressAr: "الرياض",
        SellerAddressEn: "Riyadh",
        BillToAr: "محمد",
        BillToEn: "Mohamed",
        B2bPoNumber: null,
        BuyerVatNumber: null,
        SubtotalMinor: 100_00,
        DiscountMinor: 0,
        TaxMinor: 15_00,
        ShippingMinor: 0,
        GrandTotalMinor: 115_00,
        FooterHtmlAr: "<p>تذييل</p>",
        FooterHtmlEn: "<p>Footer</p>",
        BankNameAr: "بنك",
        BankNameEn: "Bank",
        Iban: "SA00",
        ZatcaQrB64: "QR-PAYLOAD",
        Lines: new[] { new InvoiceRenderLine(1, "SKU", "اختبار", "Test", 1, 100_00, 0, 15_00, 115_00, 1500) },
        IsCreditNote: isCreditNote,
        CreditNoteOriginalInvoiceNumber: isCreditNote ? "INV-KSA-202604-000187" : null);

    [Fact]
    public void Compose_InvoiceContainsBilingualHeaders()
    {
        var renderer = new HtmlTemplateRenderer();
        var html = renderer.Compose(SampleModel());
        html.Should().Contain("dir=\"rtl\"");
        html.Should().Contain("فاتورة ضريبية");
        html.Should().Contain("Tax Invoice");
        html.Should().Contain("INV-KSA-202604-000187");
        html.Should().Contain("QR-PAYLOAD");
    }

    [Fact]
    public void Compose_CreditNoteFlipsTitleAndReferencesOriginal()
    {
        var renderer = new HtmlTemplateRenderer();
        var html = renderer.Compose(SampleModel(isCreditNote: true));
        html.Should().Contain("إشعار دائن");
        html.Should().Contain("Credit Note");
        html.Should().Contain("الفاتورة الأصلية");
    }
}
