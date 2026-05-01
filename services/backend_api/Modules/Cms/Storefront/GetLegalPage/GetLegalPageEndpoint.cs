using BackendApi.Modules.Cms.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Cms.Storefront.GetLegalPage;

public static class GetLegalPageEndpoint
{
    public static IEndpointRouteBuilder MapGetLegalPageEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/legal-pages/{kind}", HandleAsync).AllowAnonymous();
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        string kind,
        [FromServices] GetLegalPageHandler handler,
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

        if (!IsSupportedLegalKind(kind))
        {
            return CmsResponseFactory.Problem(context, 404,
                CmsReasonCode.LegalPageNotFoundForMarket,
                $"Unsupported legal page kind '{kind}'.");
        }

        var response = await handler.HandleAsync(kind, market!, locale!, ct);
        if (response is null)
        {
            return CmsResponseFactory.Problem(context, 404,
                CmsReasonCode.LegalPageNotFoundForMarket,
                $"No live legal page version for kind='{kind}', market='{market}', locale='{locale}'.");
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

    private static bool IsSupportedLegalKind(string kind) => kind switch
    {
        "terms" or "privacy" or "returns" or "cookies" => true,
        _ => false,
    };
}
