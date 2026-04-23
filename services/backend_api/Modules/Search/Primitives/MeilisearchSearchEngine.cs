using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Search.Primitives;

public sealed class MeilisearchSearchEngine(
    HttpClient httpClient,
    IOptions<MeilisearchOptions> options,
    ILogger<MeilisearchSearchEngine> logger) : ISearchEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient = httpClient;
    private readonly MeilisearchOptions _options = options.Value;
    private readonly ILogger<MeilisearchSearchEngine> _logger = logger;

    public async Task EnsureIndexAsync(SearchIndexConfig index, CancellationToken cancellationToken)
    {
        var getResponse = await SendAsync(HttpMethod.Get, $"/indexes/{index.Name}", null, cancellationToken);
        if (getResponse.StatusCode == HttpStatusCode.OK)
        {
            return;
        }

        var createPayload = new { uid = index.Name, primaryKey = "id" };
        var createResponse = await SendAsync(HttpMethod.Post, "/indexes", createPayload, cancellationToken);

        if (createResponse.IsSuccessStatusCode)
        {
            await AwaitTaskIfPresentAsync(createResponse, cancellationToken);
            return;
        }

        var body = await createResponse.Content.ReadAsStringAsync(cancellationToken);
        if (createResponse.StatusCode == HttpStatusCode.BadRequest
            && body.Contains("index_already_exists", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException($"Failed to ensure index {index.Name}: {(int)createResponse.StatusCode} {body}");
    }

    public async Task ApplySettingsAsync(
        string indexName,
        IReadOnlyCollection<string> searchableAttributes,
        IReadOnlyCollection<string> filterableAttributes,
        IReadOnlyCollection<string> sortableAttributes,
        IReadOnlyCollection<string> stopwords,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> synonyms,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            searchableAttributes,
            filterableAttributes,
            sortableAttributes,
            distinctAttribute = "id",
            stopWords = stopwords,
            synonyms,
            typoTolerance = new
            {
                minWordSizeForTypos = new
                {
                    oneTypo = 4,
                    twoTypos = 9,
                },
            },
        };

        var response = await SendAsync(HttpMethod.Patch, $"/indexes/{indexName}/settings", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
        await AwaitTaskIfPresentAsync(response, cancellationToken);
    }

    public async Task UpsertAsync(string indexName, IReadOnlyCollection<ProductSearchProjection> documents, CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
        {
            return;
        }

        var response = await SendAsync(HttpMethod.Post, $"/indexes/{indexName}/documents", documents, cancellationToken);
        response.EnsureSuccessStatusCode();
        await AwaitTaskIfPresentAsync(response, cancellationToken);
    }

    public async Task DeleteAsync(string indexName, IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken)
    {
        if (documentIds.Count == 0)
        {
            return;
        }

        var response = await SendAsync(HttpMethod.Post, $"/indexes/{indexName}/documents/delete-batch", documentIds, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
        await AwaitTaskIfPresentAsync(response, cancellationToken);
    }

    public async Task ClearIndexAsync(string indexName, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Delete, $"/indexes/{indexName}/documents", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
        await AwaitTaskIfPresentAsync(response, cancellationToken);
    }

    public async Task<SearchEngineSearchResponse> SearchAsync(string indexName, SearchEngineSearchRequest request, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["q"] = request.Query,
            ["limit"] = request.PageSize,
            ["offset"] = (request.Page - 1) * request.PageSize,
        };

        if (request.Filters.Count > 0)
        {
            payload["filter"] = request.Filters;
        }

        if (request.Sort.Count > 0)
        {
            payload["sort"] = request.Sort;
        }

        if (request.Facets.Count > 0)
        {
            payload["facets"] = request.Facets;
        }

        var response = await SendAsync(HttpMethod.Post, $"/indexes/{indexName}/search", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var parsed = await response.Content.ReadFromJsonAsync<MeiliSearchResponse>(JsonOptions, cancellationToken)
            ?? new MeiliSearchResponse([], 0, 0, new Dictionary<string, Dictionary<string, int>>());

        var facets = parsed.FacetDistribution?.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyDictionary<string, int>)kvp.Value,
            StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        return new SearchEngineSearchResponse(
            parsed.Hits ?? [],
            parsed.EstimatedTotalHits,
            parsed.ProcessingTimeMs,
            facets);
    }

    public async Task<SearchEngineAutocompleteResponse> AutocompleteAsync(string indexName, SearchEngineAutocompleteRequest request, CancellationToken cancellationToken)
    {
        var payload = new
        {
            q = request.Query,
            limit = request.Limit,
            attributesToRetrieve = new[] { "id", "name", "primaryMedia", "restricted" },
        };

        var response = await SendAsync(HttpMethod.Post, $"/indexes/{indexName}/search", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var parsed = await response.Content.ReadFromJsonAsync<MeiliSearchResponse>(JsonOptions, cancellationToken)
            ?? new MeiliSearchResponse([], 0, 0, new Dictionary<string, Dictionary<string, int>>());

        return new SearchEngineAutocompleteResponse(parsed.Hits ?? [], parsed.ProcessingTimeMs);
    }

    public async Task<ProductSearchProjection?> LookupExactAsync(string indexName, string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var escaped = EscapeFilter(code.Trim());
        var payload = new
        {
            q = string.Empty,
            filter = new[] { $"sku = \"{escaped}\" OR barcode = \"{escaped}\"" },
            limit = 1,
        };

        var response = await SendAsync(HttpMethod.Post, $"/indexes/{indexName}/search", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var parsed = await response.Content.ReadFromJsonAsync<MeiliSearchResponse>(JsonOptions, cancellationToken);
        return parsed?.Hits?.FirstOrDefault();
    }

    public async Task<SearchIndexStats> GetIndexStatsAsync(string indexName, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await SendAsync(HttpMethod.Get, $"/indexes/{indexName}/stats", null, cancellationToken);
        stopwatch.Stop();

        response.EnsureSuccessStatusCode();
        var parsed = await response.Content.ReadFromJsonAsync<MeiliStatsResponse>(JsonOptions, cancellationToken)
            ?? new MeiliStatsResponse(0);

        return new SearchIndexStats(indexName, parsed.NumberOfDocuments, (int)stopwatch.ElapsedMilliseconds);
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendAsync(HttpMethod.Get, "/health", null, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "search.engine.health-check-failed");
            return false;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
    {
        var baseUrl = _options.Url?.TrimEnd('/') ?? "http://localhost:7700";
        var request = new HttpRequestMessage(method, $"{baseUrl}{path}");

        if (!string.IsNullOrWhiteSpace(_options.MasterKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.MasterKey);
        }

        if (payload is not null)
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task AwaitTaskIfPresentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content is null)
        {
            return;
        }

        var envelope = await response.Content.ReadFromJsonAsync<MeiliTaskEnvelope>(JsonOptions, cancellationToken);
        if (envelope?.TaskUid is null)
        {
            return;
        }

        var maxAttempts = 200;
        for (var i = 0; i < maxAttempts; i++)
        {
            var taskResponse = await SendAsync(HttpMethod.Get, $"/tasks/{envelope.TaskUid.Value}", null, cancellationToken);
            taskResponse.EnsureSuccessStatusCode();

            var task = await taskResponse.Content.ReadFromJsonAsync<MeiliTaskStatus>(JsonOptions, cancellationToken);
            if (task is null)
            {
                await Task.Delay(25, cancellationToken);
                continue;
            }

            if (string.Equals(task.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(task.Status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(task.Error?.Message ?? "Meilisearch task failed.");
            }

            await Task.Delay(25, cancellationToken);
        }

        throw new TimeoutException("Timed out waiting for Meilisearch task completion.");
    }

    private static string EscapeFilter(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private sealed record MeiliSearchResponse(
        IReadOnlyList<ProductSearchProjection>? Hits,
        int EstimatedTotalHits,
        int ProcessingTimeMs,
        Dictionary<string, Dictionary<string, int>>? FacetDistribution);

    private sealed record MeiliStatsResponse(long NumberOfDocuments);
    private sealed record MeiliTaskEnvelope(long? TaskUid);
    private sealed record MeiliTaskStatus(string Status, MeiliTaskError? Error);
    private sealed record MeiliTaskError(string? Message);
}

public sealed class MeilisearchOptions
{
    public const string SectionName = "Meilisearch";
    public string Url { get; set; } = "http://localhost:7700";
    public string? MasterKey { get; set; }
}
