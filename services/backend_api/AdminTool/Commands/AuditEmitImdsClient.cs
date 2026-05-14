using System.Text.Json;

namespace BackendApi.AdminTool.Commands;

/// <summary>
/// E1 spec — Phase 0 (T007). Resolves the executor's Azure managed-identity object id
/// via the Instance Metadata Service. Falls back to <see cref="Guid.Empty"/>-equivalent
/// sentinel when IMDS is unreachable (e.g., running locally during a dev test).
/// </summary>
internal static class AuditEmitImdsClient
{
    // Documented Azure IMDS endpoint. Not user-supplied; safe to inline.
    private const string ImdsTokenUrl =
        "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https%3A%2F%2Fmanagement.azure.com%2F";
    // Stable, well-known nil-UUID used when IMDS is unreachable. Downstream queries can filter on
    // this value to distinguish local-dev / CI invocations from real Azure-MI emissions.
    private static readonly Guid LocalActorSentinel = new("00000000-0000-0000-0000-000000000001");

    public static async Task<Guid> GetManagedIdentityObjectIdAsync(CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            http.DefaultRequestHeaders.Add("Metadata", "true");
            var json = await http.GetStringAsync(ImdsTokenUrl, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            // IMDS returns `access_token` (JWT). The `oid` claim is the managed-identity object id.
            if (doc.RootElement.TryGetProperty("access_token", out var tokenProp))
            {
                var oid = ExtractOidFromJwt(tokenProp.GetString());
                if (oid is { } id) return id;
            }
            return LocalActorSentinel;
        }
        catch
        {
            // IMDS unreachable (dev / CI). Use sentinel rather than crash — audit emission MUST NOT
            // block a successful deploy on infra-side metadata flakiness.
            return LocalActorSentinel;
        }
    }

    private static Guid? ExtractOidFromJwt(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt)) return null;
        var parts = jwt.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            var padded = parts[1].PadRight(parts[1].Length + (4 - parts[1].Length % 4) % 4, '=')
                .Replace('-', '+').Replace('_', '/');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(padded));
            if (doc.RootElement.TryGetProperty("oid", out var oid) &&
                Guid.TryParse(oid.GetString(), out var parsed))
            {
                return parsed;
            }
        }
        catch
        {
            return null;
        }
        return null;
    }
}
