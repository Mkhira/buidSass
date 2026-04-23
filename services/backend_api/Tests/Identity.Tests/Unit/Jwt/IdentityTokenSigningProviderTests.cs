using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using BackendApi.Modules.Identity.Primitives;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Tests.Unit.Jwt;

public sealed class IdentityTokenSigningProviderTests
{
    [Fact]
    public void SharedPem_AllowsCrossProviderValidation()
    {
        var customerPem = CreatePrivateKeyPem();
        var options = Options.Create(new IdentityJwtOptions
        {
            Customer = new IdentityJwtSurfaceOptions
            {
                Issuer = "platform-identity",
                Audience = "customer.api",
                KeyId = "customer-current",
                PrivateKeyPem = customerPem,
            },
            Admin = new IdentityJwtSurfaceOptions
            {
                Issuer = "platform-identity",
                Audience = "admin.api",
                KeyId = "admin-current",
                PrivateKeyPem = CreatePrivateKeyPem(),
            },
        });

        var providerA = new IdentityTokenSigningProvider(
            options,
            new TestHostEnvironment("Test", Directory.GetCurrentDirectory()),
            NullLogger<IdentityTokenSigningProvider>.Instance);
        var providerB = new IdentityTokenSigningProvider(
            options,
            new TestHostEnvironment("Test", Directory.GetCurrentDirectory()),
            NullLogger<IdentityTokenSigningProvider>.Instance);

        var issuer = new JwtIssuer(providerA);
        var issued = issuer.IssueAccessToken(new JwtIssueRequest(
            SurfaceKind.Customer,
            "acct-123",
            [new Claim("permission", "identity.customer.self")]));

        var validationSurface = providerB.GetSurface(SurfaceKind.Customer);
        var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = tokenHandler.ValidateToken(
            issued.AccessToken,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = validationSurface.Issuer,
                ValidateAudience = true,
                ValidAudience = validationSurface.Audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = validationSurface.ValidationKeys,
                ClockSkew = TimeSpan.Zero,
            },
            out _);

        principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value.Should().Be("acct-123");
    }

    [Fact]
    public void BuildJwks_IncludesCurrentAndRetiredSurfaceKeys()
    {
        using var retiredKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = Options.Create(new IdentityJwtOptions
        {
            Customer = new IdentityJwtSurfaceOptions
            {
                Issuer = "platform-identity",
                Audience = "customer.api",
                KeyId = "customer-current",
                PrivateKeyPem = CreatePrivateKeyPem(),
                RetiredValidationKeys =
                [
                    new IdentityJwtRetiredKeyOptions
                    {
                        KeyId = "customer-retired-1",
                        PublicKeyPem = retiredKey.ExportSubjectPublicKeyInfoPem(),
                    },
                ],
            },
            Admin = new IdentityJwtSurfaceOptions
            {
                Issuer = "platform-identity",
                Audience = "admin.api",
                KeyId = "admin-current",
                PrivateKeyPem = CreatePrivateKeyPem(),
            },
        });

        var provider = new IdentityTokenSigningProvider(
            options,
            new TestHostEnvironment("Test", Directory.GetCurrentDirectory()),
            NullLogger<IdentityTokenSigningProvider>.Instance);

        var customerJwks = provider.BuildJwks(SurfaceKind.Customer);
        customerJwks.Keys.Select(x => x.Kid).Should().Contain(["customer-current", "customer-retired-1"]);
    }

    [Fact]
    public void DevelopmentFallback_GeneratesAndReusesFileBasedKeys()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"identity-jwt-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contentRoot);

        try
        {
            var options = Options.Create(new IdentityJwtOptions
            {
                Customer = new IdentityJwtSurfaceOptions
                {
                    Issuer = "platform-identity",
                    Audience = "customer.api",
                    KeyId = "customer-dev",
                },
                Admin = new IdentityJwtSurfaceOptions
                {
                    Issuer = "platform-identity",
                    Audience = "admin.api",
                    KeyId = "admin-dev",
                },
            });

            var providerA = new IdentityTokenSigningProvider(
                options,
                new TestHostEnvironment(Environments.Development, contentRoot),
                NullLogger<IdentityTokenSigningProvider>.Instance);
            var providerB = new IdentityTokenSigningProvider(
                options,
                new TestHostEnvironment(Environments.Development, contentRoot),
                NullLogger<IdentityTokenSigningProvider>.Instance);

            var surfaceA = providerA.GetSurface(SurfaceKind.Customer);
            var surfaceB = providerB.GetSurface(SurfaceKind.Customer);

            var expectedPath = Path.Combine(contentRoot, "infra/dev-keys/identity.customer.ecdsa.private.pem");
            File.Exists(expectedPath).Should().BeTrue();

            var keyA = JsonWebKeyConverter.ConvertFromECDsaSecurityKey((ECDsaSecurityKey)surfaceA.SigningKey);
            var keyB = JsonWebKeyConverter.ConvertFromECDsaSecurityKey((ECDsaSecurityKey)surfaceB.SigningKey);
            keyA.X.Should().Be(keyB.X);
            keyA.Y.Should().Be(keyB.Y);
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    private static string CreatePrivateKeyPem()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return ecdsa.ExportECPrivateKeyPem();
    }

    private sealed class TestHostEnvironment(string environmentName, string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Identity.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
