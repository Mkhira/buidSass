using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Notifications.Providers;

/// <summary>
/// V-3 fail-closed signature validation helpers shared across the 6 notification
/// provider impls. Three providers (Unifonic, Vodafone Egypt, Infobip) use plain
/// HMAC-SHA256 against the raw body. SES uses SNS topic signing (separate
/// validation path). SendGrid uses an ECDSA-style event-webhook header. FCM uses
/// an OIDC token verified upstream. The helpers below cover HMAC variants only —
/// SES / SendGrid / FCM each have their own small validators in-file.
/// </summary>
public static class NotificationWebhookSignature
{
    /// <summary>
    /// Hex-encoded HMAC-SHA256 carried in <paramref name="headerName"/>. Constant-time compare.
    /// Returns <c>false</c> on missing secret/header, malformed hex, length mismatch, or byte mismatch.
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
        if (string.IsNullOrWhiteSpace(providedHex)) return false;

        byte[] providedBytes;
        try { providedBytes = Convert.FromHexString(providedHex.AsSpan().Trim()); }
        catch (FormatException) { return false; }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = hmac.ComputeHash(rawBody);
        if (providedBytes.Length != computed.Length) return false;
        return CryptographicOperations.FixedTimeEquals(providedBytes, computed);
    }

    /// <summary>Base64-encoded HMAC-SHA256 variant (Infobip uses this shape).</summary>
    public static bool ValidateBase64HmacSha256FromHeader(
        HttpRequest request,
        byte[] rawBody,
        string headerName,
        string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return false;
        if (!request.Headers.TryGetValue(headerName, out var headerValues)) return false;
        var providedB64 = headerValues.ToString();
        if (string.IsNullOrWhiteSpace(providedB64)) return false;

        byte[] providedBytes;
        try { providedBytes = Convert.FromBase64String(providedB64.Trim()); }
        catch (FormatException) { return false; }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = hmac.ComputeHash(rawBody);
        if (providedBytes.Length != computed.Length) return false;
        return CryptographicOperations.FixedTimeEquals(providedBytes, computed);
    }
}
