using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Shipping.Providers;

/// <summary>
/// Shared HMAC-SHA256 webhook signature helper used by every provider
/// implementation per research §3. Constant-time comparison; fail-closed.
/// The header carrying the signature is provider-specific; the comparison
/// itself is shared.
/// </summary>
public static class ProviderWebhookSignature
{
    /// <summary>
    /// Validates a hex-encoded HMAC-SHA256 signature. Returns <c>false</c> on:
    /// missing key, missing header, empty body, malformed hex, length mismatch,
    /// or signature mismatch. Any exception inside is treated as a validation
    /// failure (fail-closed per V-3).
    /// </summary>
    public static bool ValidateHexHmacSha256(
        ReadOnlySpan<byte> rawBody,
        string? providedHexSignature,
        string? sharedSecret)
    {
        if (rawBody.Length == 0) return false;
        if (string.IsNullOrWhiteSpace(providedHexSignature)) return false;
        if (string.IsNullOrWhiteSpace(sharedSecret)) return false;

        var providedBytes = TryParseHex(providedHexSignature);
        if (providedBytes is null) return false;

        Span<byte> computed = stackalloc byte[32];
        try
        {
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(sharedSecret), rawBody, computed);
        }
        catch (Exception)
        {
            return false;
        }
        if (providedBytes.Length != computed.Length) return false;
        return CryptographicOperations.FixedTimeEquals(providedBytes, computed);
    }

    /// <summary>Convenience overload that pulls the header value off the request.</summary>
    public static bool ValidateHexHmacSha256FromHeader(
        HttpRequest request,
        ReadOnlySpan<byte> rawBody,
        string headerName,
        string? sharedSecret)
    {
        var header = request.Headers.TryGetValue(headerName, out var v) ? v.ToString() : null;
        return ValidateHexHmacSha256(rawBody, header, sharedSecret);
    }

    private static byte[]? TryParseHex(string hex)
    {
        var trimmed = hex.Trim();
        if (trimmed.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[7..];
        }
        if (trimmed.Length == 0 || (trimmed.Length & 1) != 0) return null;
        try
        {
            var bytes = new byte[trimmed.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = byte.Parse(
                    trimmed.AsSpan(i * 2, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture);
            }
            return bytes;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
