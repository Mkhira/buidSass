namespace BackendApi.Modules.Returns.Entities;

/// <summary>data-model.md table 7.</summary>
public sealed class ReturnPhoto
{
    public Guid Id { get; set; }
    public Guid? ReturnRequestId { get; set; }
    /// <summary>Per-market partitioning (Principle 5 / ADR-010) — set at upload time
    /// from the customer's market claim; persists across the bind-to-return step.</summary>
    public string MarketCode { get; set; } = string.Empty;
    public Guid AccountId { get; set; }
    public string BlobKey { get; set; } = string.Empty;
    public string Mime { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;

    public ReturnRequest? ReturnRequest { get; set; }
}
