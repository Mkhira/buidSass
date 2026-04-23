using System.Text.Json;
using BackendApi.Modules.Search.Admin.Common;
using BackendApi.Modules.Identity.Authorization.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Modules.Search.Admin.Reindex;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapReindexEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/reindex", StartAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("search.reindex");

        builder.MapGet("/reindex/{jobId:guid}/stream", StreamAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("search.reindex");

        return builder;
    }

    private static async Task<IResult> StartAsync(
        [AsParameters] ReindexRequest request,
        HttpContext context,
        SearchReindexService reindexService,
        CancellationToken cancellationToken)
    {
        var result = await ReindexHandler.StartAsync(request, reindexService, context.User, cancellationToken);
        if (result.IsSuccess)
        {
            var streamPath = $"/v1/admin/search/reindex/{result.JobId:D}/stream";
            return Results.Json(new ReindexStartResponse(result.JobId!.Value, streamPath), statusCode: StatusCodes.Status202Accepted);
        }

        if (result.IsConflict)
        {
            return AdminSearchResponseFactory.Problem(
                context,
                StatusCodes.Status409Conflict,
                result.ReasonCode!,
                result.Title!,
                result.Detail!,
                new Dictionary<string, object?>
                {
                    ["activeJobId"] = result.ActiveJobId?.ToString(),
                });
        }

        return AdminSearchResponseFactory.Problem(
            context,
            result.StatusCode,
            result.ReasonCode!,
            result.Title!,
            result.Detail!);
    }

    private static async Task StreamAsync(
        Guid jobId,
        HttpContext context,
        SearchReindexService reindexService,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        context.Response.ContentType = "text/event-stream";

        await context.Response.WriteAsync("retry: 2000\n\n", cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);

        string? lastStatus = null;
        int? lastDocsWritten = null;
        long eventId = 0;
        var heartbeatAt = DateTimeOffset.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            var snapshot = await ReindexHandler.GetJobAsync(jobId, reindexService, cancellationToken);
            if (snapshot is null)
            {
                eventId++;
                await WriteSseAsync(
                    context,
                    eventId,
                    "failed",
                    new ReindexProgressPayload(0, null, 0, "failed", "Job not found."),
                    cancellationToken);
                break;
            }

            var eventName = snapshot.Status switch
            {
                "completed" => "completed",
                "failed" => "failed",
                _ => "progress",
            };

            if (lastStatus != snapshot.Status || lastDocsWritten != snapshot.DocsWritten || eventName != "progress")
            {
                eventId++;
                await WriteSseAsync(
                    context,
                    eventId,
                    eventName,
                    new ReindexProgressPayload(
                        snapshot.DocsWritten,
                        snapshot.DocsExpected,
                        snapshot.ElapsedMs,
                        snapshot.Status,
                        snapshot.Error),
                    cancellationToken);

                lastStatus = snapshot.Status;
                lastDocsWritten = snapshot.DocsWritten;
                heartbeatAt = DateTimeOffset.UtcNow;
            }
            else if (DateTimeOffset.UtcNow - heartbeatAt > TimeSpan.FromSeconds(15))
            {
                await context.Response.WriteAsync(": heartbeat\n\n", cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
                heartbeatAt = DateTimeOffset.UtcNow;
            }

            if (snapshot.Status is "completed" or "failed")
            {
                break;
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    private static async Task WriteSseAsync(
        HttpContext context,
        long eventId,
        string eventName,
        ReindexProgressPayload payload,
        CancellationToken cancellationToken)
    {
        await context.Response.WriteAsync($"id: {eventId}\n", cancellationToken);
        await context.Response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(payload)}\n\n", cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }
}
