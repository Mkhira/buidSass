using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Cms.Storefront.ListBlogArticles;

public static class ListBlogArticlesEndpoint
{
    public static IEndpointRouteBuilder MapListBlogArticlesEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/blog-articles", HandleAsync).AllowAnonymous();
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        [FromServices] ListBlogArticlesHandler handler,
        HttpContext context,
        CancellationToken ct,
        [FromQuery] string? market = null,
        [FromQuery] string? locale = null,
        [FromQuery] string? category = null,
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 20)
    {
        var (ok, reason, detail) = MarketLocaleValidator.ValidateStorefront(market, locale);
        if (!ok)
        {
            return CmsResponseFactory.Problem(context, 400, reason!, "Storefront request rejected.", detail);
        }
        var response = await handler.HandleAsync(
            new ListBlogArticlesQuery(market!, locale!, category, page, pageSize), ct);
        var etag = EtagGenerator.Compute(response);
        var ifNoneMatch = context.Request.Headers.IfNoneMatch.ToString();
        context.Response.Headers["Cache-Control"] = "public, max-age=120, stale-while-revalidate=600";
        context.Response.Headers["ETag"] = etag;
        if (!string.IsNullOrWhiteSpace(ifNoneMatch) && ifNoneMatch.Contains(etag, StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }
        return Results.Ok(response);
    }
}
