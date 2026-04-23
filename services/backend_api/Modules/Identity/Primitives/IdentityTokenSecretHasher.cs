using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace BackendApi.Modules.Identity.Primitives;

public sealed class IdentityTokenSecretHasher(IConfiguration configuration)
{
    private readonly byte[] _pepper = ResolvePepper(configuration);

    public byte[] HashSecret(string tokenSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenSecret);
        return HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(tokenSecret.Trim()));
    }

    public bool Verify(string tokenSecret, byte[] expectedHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenSecret);
        ArgumentNullException.ThrowIfNull(expectedHash);

        var computed = HashSecret(tokenSecret);
        return CryptographicOperations.FixedTimeEquals(computed, expectedHash);
    }

    private static byte[] ResolvePepper(IConfiguration configuration)
    {
        var configured = configuration["Identity:TokenSecurity:Pepper"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Encoding.UTF8.GetBytes(configured);
        }

        return Encoding.UTF8.GetBytes("identity-token-security-dev-pepper-change-me");
    }
}
