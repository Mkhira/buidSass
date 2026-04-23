namespace BackendApi.Modules.Search.Customer.Autocomplete;

public sealed record AutocompleteRequest(
    string Query,
    string? MarketCode,
    string? Locale,
    int? Limit);

public sealed record AutocompleteResponse(
    IReadOnlyList<AutocompleteSuggestion> Suggestions,
    string? NoResultsReason);

public sealed record AutocompleteSuggestion(
    Guid ProductId,
    string Name,
    string? ThumbUrl,
    bool Restricted);
