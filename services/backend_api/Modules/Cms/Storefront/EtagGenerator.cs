using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BackendApi.Modules.Cms.Storefront;

/// <summary>
/// Computes a stable, weak-style ETag for a storefront response payload by
/// serialising it to canonical JSON and hashing the bytes with SHA-256. Per
/// spec 024 research §R15 + contract §7.1 — Cache-Control + stable ETag.
///
/// "Stable" means: identical content yields the same ETag across processes,
/// nodes, and runs. The serialiser uses sorted property output so dictionary
/// ordering doesn't drift. Only the leading 16 hex chars are used (8 bytes,
/// 2^64 collision domain) to keep the header compact.
/// </summary>
public static class EtagGenerator
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        WriteIndented = false,
        DictionaryKeyPolicy = null,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Compute<T>(T payload)
    {
        if (payload is null) return WeakTag("null");
        var json = JsonSerializer.Serialize(payload, CanonicalOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return WeakTag(Convert.ToHexString(hash, 0, 8).ToLowerInvariant());
    }

    private static string WeakTag(string body) => $"W/\"{body}\"";
}
