namespace BackendApi.Modules.Returns.Entities;

/// <summary>data-model.md table 7.</summary>
public sealed class ReturnPhoto
{
    public Guid Id { get; set; }
    public Guid? ReturnRequestId { get; set; }
    public Guid AccountId { get; set; }
    public string BlobKey { get; set; } = string.Empty;
    public string Mime { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;

    public ReturnRequest? ReturnRequest { get; set; }
}
