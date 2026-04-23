namespace BackendApi.Modules.Search.Admin.Reindex;

public sealed record ReindexRequest(string? Index);

public sealed record ReindexStartResponse(Guid JobId, string StreamPath);

public sealed record ReindexProgressPayload(
    int DocsWritten,
    int? DocsExpected,
    int ElapsedMs,
    string Status,
    string? Error);
