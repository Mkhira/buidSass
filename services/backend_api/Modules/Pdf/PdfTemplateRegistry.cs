using BackendApi.Modules.Pdf.Templates;
using QuestPDF.Infrastructure;

namespace BackendApi.Modules.Pdf;

public sealed class PdfTemplateRegistry
{
    private readonly Dictionary<string, Func<LocaleCode, object, IDocument>> _templates = new(StringComparer.OrdinalIgnoreCase);

    public PdfTemplateRegistry()
    {
        Register("tax-invoice", (locale, data) => new TaxInvoiceTemplate(locale, data));
    }

    public void Register(string name, Func<LocaleCode, object, IDocument> factory)
    {
        _templates[name] = factory;
    }

    public IDocument Resolve(string name, LocaleCode locale, object data)
    {
        if (!_templates.TryGetValue(name, out var factory))
        {
            throw new TemplateNotFoundException($"PDF template '{name}' is not registered.");
        }

        return factory(locale, data);
    }
}
