using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.B2B.Primitives;

/// <summary>
/// Hashes the plaintext invitation token with HMAC-SHA256 keyed by
/// <see cref="B2BInvitationOptions.SigningKey"/>. The plaintext is never persisted —
/// the storefront / admin handler hashes on store, the acceptance handler hashes the
/// supplied plaintext and looks up the matching <c>token_hash</c>.
/// </summary>
public sealed class CompanyInvitationTokenHasher
{
    private readonly byte[] _key;

    public CompanyInvitationTokenHasher(IOptions<B2BInvitationOptions> options)
    {
        var configured = options.Value.SigningKey;
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                "B2B:Invitation:SigningKey is not configured. Set it in environment / Key Vault / user-secrets.");
        }

        // Accept either a Base64-encoded key (preferred — supports binary keys) or a
        // raw UTF-8 string (developer ergonomics). Either way the resulting byte
        // length must be at least 32 to keep HMAC-SHA256 entropy sound.
        if (!TryDecodeBase64(configured, out var key))
        {
            key = Encoding.UTF8.GetBytes(configured);
        }
        if (key.Length < 32)
        {
            throw new InvalidOperationException(
                "B2B:Invitation:SigningKey must decode to at least 32 bytes.");
        }
        _key = key;
    }

    /// <summary>Hex-encoded HMAC-SHA256 of <paramref name="plaintextToken"/>.</summary>
    public string Hash(string plaintextToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintextToken);
        var bytes = Encoding.UTF8.GetBytes(plaintextToken);
        var digest = HMACSHA256.HashData(_key, bytes);
        return Convert.ToHexString(digest);
    }

    private static bool TryDecodeBase64(string value, out byte[] decoded)
    {
        try
        {
            decoded = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            decoded = Array.Empty<byte>();
            return false;
        }
    }
}
