using BackendApi.Modules.Pdf;
using QuestPDF.Infrastructure;

namespace backend_api.Tests.Pdf;

public sealed class PdfServiceTests
{
    static PdfServiceTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Fact]
    public async Task Render_TaxInvoice_AR_Returns_NonEmpty_ValidPdf()
    {
        var service = new QuestPdfService(new PdfTemplateRegistry());

        var bytes = await service.RenderAsync("tax-invoice", LocaleCode.AR, new { invoiceNo = "INV-AR-1", total = 100 }, CancellationToken.None);

        Assert.True(bytes.Length > 0);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(bytes.Take(4).ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Render_TaxInvoice_EN_Returns_NonEmpty_ValidPdf()
    {
        var service = new QuestPdfService(new PdfTemplateRegistry());

        var bytes = await service.RenderAsync("tax-invoice", LocaleCode.EN, new { invoiceNo = "INV-EN-1", total = 200 }, CancellationToken.None);

        Assert.True(bytes.Length > 0);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(bytes.Take(4).ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Render_UnknownTemplate_Throws_TemplateNotFoundException()
    {
        var service = new StubPdfService(new PdfTemplateRegistry());

        await Assert.ThrowsAsync<TemplateNotFoundException>(() =>
            service.RenderAsync("nonexistent-template", LocaleCode.EN, new { }, CancellationToken.None));
    }
}
