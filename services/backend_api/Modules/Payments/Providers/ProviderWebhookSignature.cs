using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Payments.Providers;

/// <summary>
/// V-3 fail-closed HMAC signature validation helpers shared across provider
/// implementations. Each provider sends the signature in a slightly different
/// header (HyperPay uses <c>X-HyperPay-Signature</c>, Tabby uses
/// <c>x-tabby-signature</c>, etc.) so each provider declares its own header
/// name and delegates the comparison here.
/// </summary>
public static class ProviderWebhookSignature
{
    /// <summary>
    /// Validates a hex-encoded HMAC-SHA256 signature carried in <paramref name="headerName"/>.
    /// Returns <c>false</c> on any of: missing secret, missing header, malformed hex,
    /// length-mismatch, byte-mismatch. Constant-time comparison via
    /// <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte},ReadOnlySpan{byte})"/>.
    /// </summary>
    public static bool ValidateHexHmacSha256FromHeader(
        HttpRequest request,
        byte[] rawBody,
        string headerName,
        string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return false;
        if (!request.Headers.TryGetValue(headerName, out var headerValues)) return false;
        var providedHex = headerValues.ToString();
        if (string.IsNullOrEmpty(providedHex)) return false;

        byte[] providedBytes;
        try
        {
            providedBytes = Convert.FromHexString(providedHex.AsSpan().Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = hmac.ComputeHash(rawBody);
        if (providedBytes.Length != computed.Length) return false;
        return CryptographicOperations.FixedTimeEquals(providedBytes, computed);
    }

    /// <summary>
    /// Same as <see cref="ValidateHexHmacSha256FromHeader"/> but for base64-encoded signatures
    /// (Tabby + Tamara use base64).
    /// </summary>
    public static bool ValidateBase64HmacSha256FromHeader(
        HttpRequest request,
        byte[] rawBody,
        string headerName,
        string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return false;
        if (!request.Headers.TryGetValue(headerName, out var headerValues)) return false;
        var providedB64 = headerValues.ToString();
        if (string.IsNullOrEmpty(providedB64)) return false;

        byte[] providedBytes;
        try
        {
            providedBytes = Convert.FromBase64String(providedB64.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = hmac.ComputeHash(rawBody);
        if (providedBytes.Length != computed.Length) return false;
        return CryptographicOperations.FixedTimeEquals(providedBytes, computed);
    }
}
