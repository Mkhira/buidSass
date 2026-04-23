using System.Security.Claims;

namespace BackendApi.Modules.Search.Admin.Reindex;

public static class ReindexHandler
{
    public static async Task<ReindexHandlerResult> StartAsync(
        ReindexRequest request,
        SearchReindexService reindexService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var accountIdRaw = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(accountIdRaw, out var accountId))
        {
            return ReindexHandlerResult.Fail(
                StatusCodes.Status401Unauthorized,
                "search.auth_required",
                "Authentication required",
                "An admin JWT is required to start reindex.");
        }

        var start = await reindexService.StartAsync(request.Index, accountId, cancellationToken);
        if (start.IsInvalidIndex)
        {
            return ReindexHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "search.market_locale_index_missing",
                "Unknown index",
                "The requested index is not configured.");
        }

        if (start.IsConflict)
        {
            return ReindexHandlerResult.Conflict(start.ActiveJobId ?? Guid.Empty);
        }

        return ReindexHandlerResult.Accepted(start.JobId!.Value);
    }

    public static async Task<ReindexJobSnapshot?> GetJobAsync(
        Guid jobId,
        SearchReindexService reindexService,
        CancellationToken cancellationToken)
    {
        var job = await reindexService.GetJobAsync(jobId, cancellationToken);
        if (job is null)
        {
            return null;
        }

        var elapsed = (int)Math.Max(0, (DateTimeOffset.UtcNow - job.StartedAt).TotalMilliseconds);
        return new ReindexJobSnapshot(
            job.Id,
            job.Status,
            job.DocsWritten,
            job.DocsExpected,
            elapsed,
            job.Error);
    }
}

public sealed record ReindexJobSnapshot(
    Guid JobId,
    string Status,
    int DocsWritten,
    int? DocsExpected,
    int ElapsedMs,
    string? Error);

public sealed record ReindexHandlerResult(
    bool IsSuccess,
    bool IsConflict,
    int StatusCode,
    Guid? JobId,
    Guid? ActiveJobId,
    string? ReasonCode,
    string? Title,
    string? Detail)
{
    public static ReindexHandlerResult Accepted(Guid jobId) =>
        new(true, false, StatusCodes.Status202Accepted, jobId, null, null, null, null);

    public static ReindexHandlerResult Conflict(Guid activeJobId) =>
        new(false, true, StatusCodes.Status409Conflict, null, activeJobId, "search.reindex.in_progress", "Reindex already in progress", "A reindex job is already running for this index.");

    public static ReindexHandlerResult Fail(int statusCode, string reasonCode, string title, string detail) =>
        new(false, false, statusCode, null, null, reasonCode, title, detail);
}
