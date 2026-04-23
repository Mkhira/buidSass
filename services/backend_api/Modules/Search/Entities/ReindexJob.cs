namespace BackendApi.Modules.Search.Entities;

public sealed class ReindexJob
{
    public Guid Id { get; set; }
    public string IndexName { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public Guid StartedByAccountId { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public int? DocsExpected { get; set; }
    public int DocsWritten { get; set; }
    public string? Error { get; set; }
}
