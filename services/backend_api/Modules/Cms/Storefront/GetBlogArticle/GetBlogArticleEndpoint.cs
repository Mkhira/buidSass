using BackendApi.Modules.Cms.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Cms.Storefront.GetBlogArticle;

public static class GetBlogArticleEndpoint
{
    public static IEndpointRouteBuilder MapGetBlogArticleEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/blog-articles/{slug}", HandleAsync).AllowAnonymous();
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        string slug,
        [FromServices] GetBlogArticleHandler handler,
        HttpContext context,
        CancellationToken ct,
        [FromQuery] string? market = null,
        [FromQuery] string? locale = null)
    {
        var (ok, reason, detail) = MarketLocaleValidator.ValidateStorefront(market, locale);
        if (!ok)
        {
            return CmsResponseFactory.Problem(context, 400, reason!, "Storefront request rejected.", detail);
        }
        var response = await handler.HandleAsync(slug, market!, locale!, ct);
        if (response is null)
        {
            return CmsResponseFactory.Problem(context, 404,
                CmsReasonCode.PreviewEntityNotFound, $"No live blog article for slug '{slug}'.");
        }
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
