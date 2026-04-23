using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BackendApi.Modules.Identity.Primitives;

public sealed class IdentityClientSecurityHasher
{
    private static readonly byte[] DevFallbackPepper = Encoding.UTF8.GetBytes("identity-dev-client-security-pepper-change-me");
    private readonly byte[] _pepper;

    public IdentityClientSecurityHasher(IConfiguration configuration, IHostEnvironment hostEnvironment)
    {
        var configured = configuration["Identity:ClientSecurity:IpPepper"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (hostEnvironment.IsDevelopment() || hostEnvironment.IsEnvironment("Test"))
            {
                _pepper = DevFallbackPepper;
                return;
            }

            throw new InvalidOperationException("Identity:ClientSecurity:IpPepper must be configured outside Development/Test.");
        }

        var raw = Encoding.UTF8.GetBytes(configured.Trim());
        if (raw.Length < 32 && !(hostEnvironment.IsDevelopment() || hostEnvironment.IsEnvironment("Test")))
        {
            throw new InvalidOperationException("Identity:ClientSecurity:IpPepper must be at least 32 bytes.");
        }

        _pepper = raw;
    }

    public byte[] HashIp(string? ipAddress) => HashInternal(ipAddress ?? "unknown-ip");

    public byte[] HashIdentifier(string identifier) => HashInternal(identifier.Trim());

    private byte[] HashInternal(string value)
    {
        return HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(value));
    }
}
