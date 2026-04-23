using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace BackendApi.Modules.Identity.Primitives;

public sealed class IdentityClientFingerprintHasher
{
    private static readonly byte[] DefaultPepper = Encoding.UTF8.GetBytes("identity-dev-fingerprint-pepper-change-me");
    private readonly byte[] _pepper;

    public IdentityClientFingerprintHasher(IConfiguration configuration)
    {
        var configured = configuration["Identity:SessionSecurity:FingerprintPepper"];
        _pepper = string.IsNullOrWhiteSpace(configured)
            ? DefaultPepper
            : Encoding.UTF8.GetBytes(configured.Trim());
    }

    public byte[] Hash(string? userAgent, string? ipAddress)
    {
        var normalized = $"{(userAgent ?? string.Empty).Trim()}|{(ipAddress ?? "unknown-ip").Trim()}";
        return HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(normalized));
    }

    public bool Verify(byte[] expectedHash, string? userAgent, string? ipAddress)
    {
        ArgumentNullException.ThrowIfNull(expectedHash);

        var currentHash = Hash(userAgent, ipAddress);
        return CryptographicOperations.FixedTimeEquals(expectedHash, currentHash);
    }
}
