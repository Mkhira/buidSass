using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BackendApi.Modules.Observability;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Search.Primitives;

public sealed class QueryLogger(
    SearchMetrics searchMetrics,
    ILogger<QueryLogger> logger)
{
    private static readonly Regex CollapseWhitespaceRegex = new("\\s+", RegexOptions.Compiled);

    private readonly SearchMetrics _searchMetrics = searchMetrics;
    private readonly ILogger<QueryLogger> _logger = logger;

    public void Log(
        string? query,
        string marketCode,
        string locale,
        int resultCount,
        int latencyMs,
        bool hasFilters)
    {
        var normalized = NormalizeForHash(query);
        var queryHash = ComputeSha256Hex(normalized);

        _logger.LogInformation(
            "search.query query_hash={query_hash} marketCode={marketCode} locale={locale} resultCount={resultCount} latencyMs={latencyMs} filters={filters}",
            queryHash,
            marketCode,
            locale,
            resultCount,
            latencyMs,
            hasFilters);

        _searchMetrics.RecordQueryLatency(latencyMs, locale, hasFilters);
    }

    private static string NormalizeForHash(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var trimmed = query.Trim().ToLowerInvariant();
        return CollapseWhitespaceRegex.Replace(trimmed, " ");
    }

    private static string ComputeSha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.AppendFormat("{0:x2}", b);
        }

        return sb.ToString();
    }
}
