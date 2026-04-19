namespace BackendApi.Modules.Storage;

public interface IStorageService
{
    Task<StoredFileResult> UploadAsync(
        Stream content,
        string fileName,
        string mimeType,
        MarketCode market,
        CancellationToken cancellationToken);

    Task<Uri> GetSignedUrlAsync(string fileId, TimeSpan expiry, CancellationToken cancellationToken);

    Task DeleteAsync(string fileId, CancellationToken cancellationToken);
}
