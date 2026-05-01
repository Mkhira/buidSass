using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Cms.Storefront.ListBannerSlots;

public static class ListBannerSlotsEndpoint
{
    public static IEndpointRouteBuilder MapListBannerSlotsEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/banner-slots", HandleAsync)
            .AllowAnonymous();
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        [FromServices] ListBannerSlotsHandler handler,
        HttpContext context,
        CancellationToken ct,
        [FromQuery] string? market = null,
        [FromQuery] string? locale = null,
        [FromQuery(Name = "slot_kind")] string? slotKind = null,
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 50)
    {
        var (ok, reason, detail) = MarketLocaleValidator.ValidateStorefront(market, locale);
        if (!ok)
        {
            return CmsResponseFactory.Problem(context, 400, reason!, "Storefront request rejected.", detail);
        }

        var response = await handler.HandleAsync(
            new ListBannerSlotsQuery(market!, locale!, slotKind, page, pageSize), ct);

        var etag = EtagGenerator.Compute(response);
        if (TryRespondNotModified(context, etag, out var notModified)) return notModified!;

        context.Response.Headers["Cache-Control"] = "public, max-age=60, stale-while-revalidate=300";
        context.Response.Headers["ETag"] = etag;
        return Results.Ok(response);
    }

    private static bool TryRespondNotModified(HttpContext context, string etag, out IResult? result)
    {
        var ifNoneMatch = context.Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrWhiteSpace(ifNoneMatch) && ifNoneMatch.Contains(etag, StringComparison.Ordinal))
        {
            context.Response.Headers["Cache-Control"] = "public, max-age=60, stale-while-revalidate=300";
            context.Response.Headers["ETag"] = etag;
            result = Results.StatusCode(StatusCodes.Status304NotModified);
            return true;
        }
        result = null;
        return false;
    }
}
