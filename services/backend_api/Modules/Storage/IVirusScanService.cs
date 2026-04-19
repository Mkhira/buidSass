namespace BackendApi.Modules.Storage;

public interface IVirusScanService
{
    Task<ScanResult> ScanAsync(Stream content, CancellationToken cancellationToken);
}
