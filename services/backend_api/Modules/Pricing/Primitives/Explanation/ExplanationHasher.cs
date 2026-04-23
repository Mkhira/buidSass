using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BackendApi.Modules.Pricing.Primitives.Explanation;

public static class ExplanationHasher
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = null,
    };

    /// <summary>Canonicalize JSON (sort keys at every depth) then UTF-8 encode + SHA-256 + base64url.</summary>
    public static (string HashBase64Url, byte[] HashBytes, byte[] CanonicalBytes) Hash(object payload)
    {
        var raw = JsonSerializer.SerializeToNode(payload, CanonicalOptions);
        var canonical = Canonicalize(raw);
        var canonicalJson = canonical is null ? "null" : canonical.ToJsonString(CanonicalOptions);
        var bytes = Encoding.UTF8.GetBytes(canonicalJson);
        var hashBytes = SHA256.HashData(bytes);
        return (Base64Url(hashBytes), hashBytes, bytes);
    }

    private static JsonNode? Canonicalize(JsonNode? node)
    {
        return node switch
        {
            JsonObject obj => CanonicalizeObject(obj),
            JsonArray arr => CanonicalizeArray(arr),
            _ => node?.DeepClone(),
        };
    }

    private static JsonObject CanonicalizeObject(JsonObject obj)
    {
        var sorted = new JsonObject();
        foreach (var key in obj.Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal))
        {
            sorted[key] = Canonicalize(obj[key]);
        }
        return sorted;
    }

    private static JsonArray CanonicalizeArray(JsonArray arr)
    {
        var copy = new JsonArray();
        foreach (var item in arr)
        {
            copy.Add(Canonicalize(item));
        }
        return copy;
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
