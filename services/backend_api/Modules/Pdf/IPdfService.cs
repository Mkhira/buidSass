namespace BackendApi.Modules.Pdf;

public interface IPdfService
{
    Task<byte[]> RenderAsync(string templateName, LocaleCode locale, object data, CancellationToken cancellationToken);
}
