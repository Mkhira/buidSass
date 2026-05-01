using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Cms.Storefront.ListFeaturedSections;

public static class ListFeaturedSectionsEndpoint
{
    public static IEndpointRouteBuilder MapListFeaturedSectionsEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/featured-sections", HandleAsync)
            .AllowAnonymous();
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        [FromServices] ListFeaturedSectionsHandler handler,
        HttpContext context,
        CancellationToken ct,
        [FromQuery] string? market = null,
        [FromQuery] string? locale = null,
        [FromQuery(Name = "section_kind")] string? sectionKind = null,
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 50)
    {
        var (ok, reason, detail) = MarketLocaleValidator.ValidateStorefront(market, locale);
        if (!ok)
        {
            return CmsResponseFactory.Problem(context, 400, reason!, "Storefront request rejected.", detail);
        }

        var response = await handler.HandleAsync(
            new ListFeaturedSectionsQuery(market!, locale!, sectionKind, page, pageSize), ct);

        var etag = EtagGenerator.Compute(response);
        var ifNoneMatch = context.Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrWhiteSpace(ifNoneMatch) && ifNoneMatch.Contains(etag, StringComparison.Ordinal))
        {
            context.Response.Headers["Cache-Control"] = "public, max-age=60, stale-while-revalidate=300";
            context.Response.Headers["ETag"] = etag;
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        context.Response.Headers["Cache-Control"] = "public, max-age=60, stale-while-revalidate=300";
        context.Response.Headers["ETag"] = etag;
        return Results.Ok(response);
    }
}
