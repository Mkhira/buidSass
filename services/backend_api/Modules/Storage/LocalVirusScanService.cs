namespace BackendApi.Modules.Storage;

public sealed class LocalVirusScanService : IVirusScanService
{
    public Task<ScanResult> ScanAsync(Stream content, CancellationToken cancellationToken)
    {
        return Task.FromResult(ScanResult.Clean);
    }
}
