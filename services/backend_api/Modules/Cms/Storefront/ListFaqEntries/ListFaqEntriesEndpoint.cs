using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Cms.Storefront.ListFaqEntries;

public static class ListFaqEntriesEndpoint
{
    public static IEndpointRouteBuilder MapListFaqEntriesEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/faq", HandleAsync).AllowAnonymous();
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        [FromServices] ListFaqEntriesHandler handler,
        HttpContext context,
        CancellationToken ct,
        [FromQuery] string? market = null,
        [FromQuery] string? locale = null,
        [FromQuery] string? category = null)
    {
        var (ok, reason, detail) = MarketLocaleValidator.ValidateStorefront(market, locale);
        if (!ok)
        {
            return CmsResponseFactory.Problem(context, 400, reason!, "Storefront request rejected.", detail);
        }
        var response = await handler.HandleAsync(
            new ListFaqEntriesQuery(market!, locale!, category), ct);
        var etag = EtagGenerator.Compute(response);
        var ifNoneMatch = context.Request.Headers.IfNoneMatch.ToString();
        context.Response.Headers["Cache-Control"] = "public, max-age=300, stale-while-revalidate=900";
        context.Response.Headers["ETag"] = etag;
        if (!string.IsNullOrWhiteSpace(ifNoneMatch) && ifNoneMatch.Contains(etag, StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }
        return Results.Ok(response);
    }
}
