using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BackendApi.Modules.Cms.Primitives;

/// <summary>
/// HMAC-SHA256 signer for CMS preview tokens per spec 024 research §R3.
/// The signing key is sourced from layered configuration (env-var or Key Vault
/// in Phase 1E); the constructor takes the resolved 32-byte secret.
/// Constant-time compare on verify; throws
/// <see cref="PreviewTokenSignatureInvalidException"/> on mismatch.
/// </summary>
public sealed class PreviewTokenSigner
{
    private const string Version = "v1";
    private static readonly char[] PayloadSeparator = new[] { '|' };

    private readonly byte[] _signingKey;

    public PreviewTokenSigner(byte[] signingKey)
    {
        if (signingKey is null) throw new ArgumentNullException(nameof(signingKey));
        if (signingKey.Length < 32)
        {
            throw new ArgumentException(
                $"Preview-token signing key must be ≥ 32 bytes; got {signingKey.Length}.",
                nameof(signingKey));
        }
        _signingKey = signingKey;
    }

    /// <summary>
    /// Returns an opaque base64url token encoding <paramref name="claims"/>
    /// + an HMAC-SHA256 tag.
    /// </summary>
    public string Sign(PreviewTokenClaims claims)
    {
        var payload = EncodePayload(claims);
        using var hmac = new HMACSHA256(_signingKey);
        var tag = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var token = $"{payload}.{Base64Url.Encode(tag)}";
        return Base64Url.Encode(Encoding.UTF8.GetBytes(token));
    }

    /// <summary>
    /// Verifies an opaque token's signature and returns the carried claims.
    /// Throws <see cref="PreviewTokenSignatureInvalidException"/> when the
    /// token is malformed, signed with a different key, or tampered.
    /// </summary>
    public PreviewTokenClaims Verify(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new PreviewTokenSignatureInvalidException();
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Base64Url.Decode(token));
        }
        catch (FormatException)
        {
            throw new PreviewTokenSignatureInvalidException();
        }

        var dot = decoded.LastIndexOf('.');
        if (dot < 1 || dot >= decoded.Length - 1)
        {
            throw new PreviewTokenSignatureInvalidException();
        }

        var payload = decoded[..dot];
        var providedTag = decoded[(dot + 1)..];

        using var hmac = new HMACSHA256(_signingKey);
        var expectedTag = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));

        byte[] providedTagBytes;
        try
        {
            providedTagBytes = Base64Url.Decode(providedTag);
        }
        catch (FormatException)
        {
            throw new PreviewTokenSignatureInvalidException();
        }

        if (!CryptographicOperations.FixedTimeEquals(providedTagBytes, expectedTag))
        {
            throw new PreviewTokenSignatureInvalidException();
        }

        return DecodePayload(payload);
    }

    /// <summary>
    /// SHA-256 of the *full* opaque token, hex-encoded — persisted in
    /// <c>cms.preview_tokens.token_hash</c>. The token itself is NEVER persisted.
    /// </summary>
    public static byte[] HashToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Token must be non-empty.", nameof(token));
        }
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    private static string EncodePayload(PreviewTokenClaims c)
    {
        // pipe-separated since none of the field values contain '|'.
        var sb = new StringBuilder();
        sb.Append(Version).Append('|');
        sb.Append(c.EntityKind.ToWire()).Append('|');
        sb.Append(c.EntityId.ToString("D")).Append('|');
        sb.Append(c.VersionId.ToString("D")).Append('|');
        sb.Append(c.MintTimestampUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)).Append('|');
        sb.Append(c.TtlSeconds.ToString(CultureInfo.InvariantCulture)).Append('|');
        sb.Append(c.ActorRoleAtMint);
        return Base64Url.Encode(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static PreviewTokenClaims DecodePayload(string base64Payload)
    {
        string raw;
        try
        {
            raw = Encoding.UTF8.GetString(Base64Url.Decode(base64Payload));
        }
        catch (FormatException)
        {
            throw new PreviewTokenSignatureInvalidException();
        }

        var parts = raw.Split(PayloadSeparator);
        if (parts.Length != 7 || parts[0] != Version)
        {
            throw new PreviewTokenSignatureInvalidException();
        }

        try
        {
            return new PreviewTokenClaims(
                EntityKind: EntityKindWire.FromWire(parts[1]),
                EntityId: Guid.ParseExact(parts[2], "D"),
                VersionId: Guid.ParseExact(parts[3], "D"),
                MintTimestampUtc: DateTimeOffset.FromUnixTimeSeconds(
                    long.Parse(parts[4], CultureInfo.InvariantCulture)),
                TtlSeconds: int.Parse(parts[5], CultureInfo.InvariantCulture),
                ActorRoleAtMint: parts[6]);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidOperationException)
        {
            throw new PreviewTokenSignatureInvalidException();
        }
    }
}

internal static class Base64Url
{
    public static string Encode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static byte[] Decode(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
            case 1: throw new FormatException("Invalid base64url length.");
        }
        return Convert.FromBase64String(padded);
    }
}

/// <summary>Thrown by <see cref="PreviewTokenSigner.Verify"/> on a tampered or wrong-key token.</summary>
public sealed class PreviewTokenSignatureInvalidException()
    : Exception("Preview token signature is invalid.");
