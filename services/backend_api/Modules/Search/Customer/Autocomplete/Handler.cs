using System.Diagnostics;
using BackendApi.Modules.Search.Primitives;
using BackendApi.Modules.Search.Primitives.Normalization;

namespace BackendApi.Modules.Search.Customer.Autocomplete;

public static class AutocompleteHandler
{
    public static async Task<AutocompleteHandlerResult> HandleAsync(
        AutocompleteRequest request,
        ISearchEngine searchEngine,
        ArabicNormalizer normalizer,
        QueryLogger queryLogger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return AutocompleteHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "search.query_required",
                "Query required",
                "An autocomplete query string is required.");
        }

        var marketCode = request.MarketCode?.Trim().ToLowerInvariant() ?? "ksa";
        var locale = request.Locale?.Trim().ToLowerInvariant() ?? "en";

        if (!IndexNames.TryResolve(marketCode, locale, out var index))
        {
            return AutocompleteHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "search.market_locale_index_missing",
                "Unknown market/locale",
                "The requested market and locale index is not configured.");
        }

        var rawQuery = request.Query.Trim();
        var query = string.Equals(locale, "ar", StringComparison.OrdinalIgnoreCase)
            ? normalizer.Normalize(rawQuery)
            : rawQuery;

        var limit = request.Limit is null or < 1 or > 10 ? 5 : request.Limit.Value;
        var stopwatch = Stopwatch.StartNew();
        var searchResponse = await searchEngine.AutocompleteAsync(
            index.Name,
            new SearchEngineAutocompleteRequest(query, limit),
            cancellationToken);
        stopwatch.Stop();

        var suggestions = searchResponse.Hits
            .Select(hit => new AutocompleteSuggestion(hit.Id, hit.Name, hit.PrimaryMedia.ThumbUrl, hit.Restricted))
            .ToArray();

        queryLogger.Log(rawQuery, marketCode, locale, suggestions.Length, (int)stopwatch.ElapsedMilliseconds, hasFilters: false);

        return AutocompleteHandlerResult.Success(new AutocompleteResponse(
            suggestions,
            suggestions.Length == 0 ? "no_matches" : null));
    }
}

public sealed record AutocompleteHandlerResult(
    bool IsSuccess,
    AutocompleteResponse? Response,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail)
{
    public static AutocompleteHandlerResult Success(AutocompleteResponse response) =>
        new(true, response, StatusCodes.Status200OK, null, null, null);

    public static AutocompleteHandlerResult Fail(int statusCode, string reasonCode, string title, string detail) =>
        new(false, null, statusCode, reasonCode, title, detail);
}
