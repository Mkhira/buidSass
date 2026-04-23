using BackendApi.Modules.Search.Persistence;
using BackendApi.Modules.Search.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Search.Admin.ListJobs;

public static class Handler
{
    public static async Task<ListJobsHandlerResult> HandleAsync(
        ListJobsRequest request,
        SearchDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var page = request.Page is null or < 1 ? 1 : request.Page.Value;
        var pageSize = request.PageSize is null or < 1 or > 100 ? 20 : request.PageSize.Value;

        var query = dbContext.ReindexJobs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Index))
        {
            if (!IndexNames.TryParseIndex(request.Index, out var parsed))
            {
                return ListJobsHandlerResult.Fail(
                    StatusCodes.Status400BadRequest,
                    "search.market_locale_index_missing",
                    "Unknown index",
                    "The requested index is not configured.");
            }

            query = query.Where(x => x.IndexName == parsed.Name);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var normalizedStatus = request.Status.Trim().ToLowerInvariant();
            if (normalizedStatus is not ("pending" or "running" or "completed" or "failed"))
            {
                return ListJobsHandlerResult.Fail(
                    StatusCodes.Status400BadRequest,
                    "search.invalid_status",
                    "Invalid status",
                    "The requested status filter is not supported.");
            }

            query = query.Where(x => x.Status == normalizedStatus);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return ListJobsHandlerResult.Success(
            new ListJobsResponse(
                rows.Select(x => new ListJobsItem(
                    x.Id,
                    x.IndexName,
                    x.Status,
                    x.StartedByAccountId,
                    x.StartedAt,
                    x.CompletedAt,
                    x.DocsExpected,
                    x.DocsWritten,
                    x.Error))
                .ToArray(),
                total,
                page,
                pageSize));
    }
}

public sealed record ListJobsHandlerResult(
    bool IsSuccess,
    ListJobsResponse? Response,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail)
{
    public static ListJobsHandlerResult Success(ListJobsResponse response) =>
        new(true, response, StatusCodes.Status200OK, null, null, null);

    public static ListJobsHandlerResult Fail(int statusCode, string reasonCode, string title, string detail) =>
        new(false, null, statusCode, reasonCode, title, detail);
}
