using QuestPDF.Fluent;

namespace BackendApi.Modules.Pdf;

public sealed class QuestPdfService(PdfTemplateRegistry templateRegistry) : IPdfService
{
    public Task<byte[]> RenderAsync(string templateName, LocaleCode locale, object data, CancellationToken cancellationToken)
    {
        var document = templateRegistry.Resolve(templateName, locale, data);
        var pdf = document.GeneratePdf();
        return Task.FromResult(pdf);
    }
}
