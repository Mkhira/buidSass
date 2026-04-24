using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Cart.Primitives;

/// <summary>
/// HMAC-signed opaque cart token (R1). Format: base64url(payload "." signature) where payload
/// = random 32 bytes + issuedAtUnixSeconds. The server can verify signature without a DB lookup;
/// the payload's random bytes are what's hashed into cart.cart_token_hash for rows lookup.
/// </summary>
public sealed class CartTokenProvider(IOptions<CartOptions> options)
{
    private readonly CartOptions _options = options.Value;

    public sealed record IssuedToken(string Token, byte[] Hash, DateTimeOffset IssuedAt);

    public IssuedToken Issue(DateTimeOffset? nowUtc = null)
    {
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var random = new byte[32];
        RandomNumberGenerator.Fill(random);

        var payload = new byte[32 + 8];
        Buffer.BlockCopy(random, 0, payload, 0, 32);
        BinaryPrimitives_WriteInt64(payload.AsSpan(32, 8), now.ToUnixTimeSeconds());

        var sig = Sign(payload);
        var bytes = new byte[payload.Length + sig.Length];
        Buffer.BlockCopy(payload, 0, bytes, 0, payload.Length);
        Buffer.BlockCopy(sig, 0, bytes, payload.Length, sig.Length);

        var token = Base64Url(bytes);
        var hash = SHA256.HashData(random);
        return new IssuedToken(token, hash, now);
    }

    public bool TryDecode(string token, DateTimeOffset nowUtc, out byte[] hash)
    {
        hash = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = FromBase64Url(token);
        }
        catch
        {
            return false;
        }

        if (bytes.Length != 32 + 8 + 32)
        {
            return false;
        }

        var payload = bytes.AsSpan(0, 40).ToArray();
        var sig = bytes.AsSpan(40, 32).ToArray();
        var expected = Sign(payload);
        if (!CryptographicOperations.FixedTimeEquals(sig, expected))
        {
            return false;
        }

        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives_ReadInt64(payload.AsSpan(32, 8)));
        if ((nowUtc - issuedAt).TotalDays > _options.TokenLifetimeDays)
        {
            return false;
        }

        hash = SHA256.HashData(payload.AsSpan(0, 32));
        return true;
    }

    /// <summary>Compute the storage hash for a token without verifying — used when reading anonymous carts by hash.</summary>
    public byte[] HashForLookup(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Array.Empty<byte>();
        }
        try
        {
            var bytes = FromBase64Url(token);
            if (bytes.Length < 32) return Array.Empty<byte>();
            return SHA256.HashData(bytes.AsSpan(0, 32));
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    private byte[] Sign(byte[] payload)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_options.TokenSecret);
        using var hmac = new HMACSHA256(keyBytes);
        return hmac.ComputeHash(payload);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }

    private static void BinaryPrimitives_WriteInt64(Span<byte> span, long value)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(span, value);
    }
    private static long BinaryPrimitives_ReadInt64(ReadOnlySpan<byte> span)
    {
        return System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(span);
    }
}
