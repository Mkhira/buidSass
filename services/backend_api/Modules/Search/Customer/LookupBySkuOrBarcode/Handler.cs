using System.Diagnostics;
using BackendApi.Modules.Search.Primitives;

namespace BackendApi.Modules.Search.Customer.LookupBySkuOrBarcode;

public static class LookupHandler
{
    public static async Task<LookupHandlerResult> HandleAsync(
        LookupRequest request,
        ISearchEngine searchEngine,
        QueryLogger queryLogger,
        CancellationToken cancellationToken)
    {
        var marketCode = request.MarketCode?.Trim().ToLowerInvariant() ?? "ksa";
        var locale = request.Locale?.Trim().ToLowerInvariant() ?? "en";

        if (!IndexNames.TryResolve(marketCode, locale, out var index))
        {
            return LookupHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "search.market_locale_index_missing",
                "Unknown market/locale",
                "The requested market and locale index is not configured.");
        }

        var stopwatch = Stopwatch.StartNew();
        var hit = await searchEngine.LookupExactAsync(index.Name, request.Code, cancellationToken);
        stopwatch.Stop();
        queryLogger.Log(request.Code, marketCode, locale, hit is null ? 0 : 1, (int)stopwatch.ElapsedMilliseconds, hasFilters: false);

        if (hit is null)
        {
            return LookupHandlerResult.Success(new LookupResponse(null));
        }

        return LookupHandlerResult.Success(new LookupResponse(
            new SearchLookupHit(
                hit.Id,
                hit.Sku,
                hit.Barcode,
                hit.Name,
                hit.Restricted,
                hit.RestrictionReasonCode,
                hit.MarketCode,
                hit.Locale)));
    }
}

public sealed record LookupHandlerResult(
    bool IsSuccess,
    LookupResponse? Response,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail)
{
    public static LookupHandlerResult Success(LookupResponse response) =>
        new(true, response, StatusCodes.Status200OK, null, null, null);

    public static LookupHandlerResult Fail(int statusCode, string reasonCode, string title, string detail) =>
        new(false, null, statusCode, reasonCode, title, detail);
}
