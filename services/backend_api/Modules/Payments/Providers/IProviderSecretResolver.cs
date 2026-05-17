using Microsoft.Extensions.Configuration;

namespace BackendApi.Modules.Payments.Providers;

/// <summary>
/// Resolves provider credentials from the layered configuration (E1 KV slots).
/// Returns a dictionary keyed by canonical slot path so providers can look up
/// the secrets they need by stable name (e.g. <c>payments/sa/hyperpay/api-secret</c>).
/// BR-12 — no secret ever lives in <c>appsettings*.json</c>; the resolver
/// reads strictly from configuration (which is layered with Key Vault in
/// staging / production).
/// </summary>
public interface IProviderSecretResolver
{
    /// <summary>
    /// Returns the three secrets (<c>api-key</c>, <c>api-secret</c>,
    /// <c>webhook-signing-key</c>) for the given provider, keyed by the
    /// canonical logical path. Missing values surface as empty strings so
    /// HMAC validators fail-closed (V-3).
    /// </summary>
    IReadOnlyDictionary<string, string> ResolveSecrets(string providerId);
}

public sealed class ProviderSecretResolver : IProviderSecretResolver
{
    private readonly IConfiguration _configuration;

    public ProviderSecretResolver(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IReadOnlyDictionary<string, string> ResolveSecrets(string providerId)
    {
        // Logical path → on-disk KV path uses `--` as separator. We accept
        // both formats so configuration providers that flatten to colon-
        // delimited keys (Azure App Configuration default) and providers
        // that keep `--` (KV secret names) both work.
        var markets = new[] { "sa", "eg" };
        var keys = new[] { "api-key", "api-secret", "webhook-signing-key" };
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var market in markets)
        {
            foreach (var key in keys)
            {
                var logical = $"payments/{market}/{providerId}/{key}";
                // Try multiple shapes that .NET configuration commonly produces.
                var v = _configuration[logical]
                    ?? _configuration[$"payments:{market}:{providerId}:{key}"]
                    ?? _configuration[$"payments--{market}--{providerId}--{key}"]
                    ?? string.Empty;
                result[logical] = v;
            }
        }
        return result;
    }
}
