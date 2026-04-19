using System.Text.Json;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace BackendApi.Modules.Pdf;

public sealed class StubPdfService(PdfTemplateRegistry templateRegistry) : IPdfService
{
    public Task<byte[]> RenderAsync(string templateName, LocaleCode locale, object data, CancellationToken cancellationToken)
    {
        _ = templateRegistry.Resolve(templateName, locale, data);

        var payload = JsonSerializer.Serialize(data);
        var pdf = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.Content().Text($"Stub PDF | locale={locale} | {payload}");
            });
        }).GeneratePdf();

        return Task.FromResult(pdf);
    }
}
