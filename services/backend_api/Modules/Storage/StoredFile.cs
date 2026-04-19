namespace BackendApi.Modules.Storage;

public sealed class StoredFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BucketKey { get; set; } = string.Empty;
    public string Market { get; set; } = string.Empty;
    public string? OriginalFilename { get; set; }
    public long SizeBytes { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public string VirusScanStatus { get; set; } = string.Empty;
    public Guid? UploadedByActorId { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
}
