using System.Security.Cryptography;
using System.Text;
using BackendApi.Modules.Notifications.Domain;
using BackendApi.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BackendApi.Modules.Notifications.UnsubscribeTokens;

/// <summary>
/// T039 — signed unsubscribe links. The raw token is
/// <c>BASE64URL(payload).BASE64URL(HMAC-SHA256(payload, secret))</c> where
/// <c>payload</c> is <c>customer_id|channel|category|nonce|expires_at</c>.
/// We store the SHA-256 of the entire raw token (BR-12); validation re-hashes
/// the inbound token and matches against <c>unsubscribe_tokens.token_hash</c>.
/// 30-day TTL is the clarify-locked default.
/// </summary>
public sealed class UnsubscribeTokenService
{
    private readonly NotificationsDbContext _db;
    private readonly TimeProvider _clock;
    private readonly string _signingSecret;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromDays(30);

    public UnsubscribeTokenService(
        NotificationsDbContext db,
        TimeProvider clock,
        IConfiguration configuration)
    {
        _db = db;
        _clock = clock;
        _signingSecret = configuration["notifications:secrets:notifications-secrets/multi/unsubscribe-tokens/hmac-key"]
            ?? "sandbox-unsubscribe-hmac-secret-do-not-use-in-prod";
    }

    public async Task<string> IssueAsync(Guid customerId, string channel, string category, CancellationToken ct)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var expiresAt = _clock.GetUtcNow().Add(DefaultTtl);
        var payload = $"{customerId:N}|{channel}|{category}|{nonce}|{expiresAt:O}";
        var token = Sign(payload);
        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));

        _db.UnsubscribeTokens.Add(new UnsubscribeToken
        {
            TokenHash = tokenHash,
            CustomerId = customerId,
            Channel = channel,
            Category = category,
            ExpiresAt = expiresAt,
            CreatedAt = _clock.GetUtcNow(),
        });
        await _db.SaveChangesAsync(ct);
        return token;
    }

    public async Task<UnsubscribeToken?> ValidateAndConsumeAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        if (!VerifySignature(token)) return null;

        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var row = await _db.UnsubscribeTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        if (row is null) return null;
        if (row.UsedAt is not null) return null;
        if (row.ExpiresAt < _clock.GetUtcNow()) return null;

        row.UsedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        return row;
    }

    private string Sign(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_signingSecret));
        var sig = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return $"{Base64UrlEncode(Encoding.UTF8.GetBytes(payload))}.{Base64UrlEncode(sig)}";
    }

    private bool VerifySignature(string token)
    {
        var dot = token.IndexOf('.');
        if (dot <= 0 || dot >= token.Length - 1) return false;
        var payloadPart = token[..dot];
        var sigPart = token[(dot + 1)..];

        byte[] payloadBytes;
        byte[] sigBytes;
        try
        {
            payloadBytes = Base64UrlDecode(payloadPart);
            sigBytes = Base64UrlDecode(sigPart);
        }
        catch (FormatException) { return false; }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_signingSecret));
        var expected = hmac.ComputeHash(payloadBytes);
        return sigBytes.Length == expected.Length
            && CryptographicOperations.FixedTimeEquals(sigBytes, expected);
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
