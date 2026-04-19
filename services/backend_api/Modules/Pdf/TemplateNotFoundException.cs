namespace BackendApi.Modules.Pdf;

public sealed class TemplateNotFoundException(string message) : Exception(message)
{
}
