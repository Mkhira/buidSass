namespace BackendApi.Modules.Storage;

public sealed record StoredFileResult(Guid FileId, Uri SignedUrl, MarketCode Market);
