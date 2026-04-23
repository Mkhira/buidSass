using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BackendApi.Modules.Identity.Primitives;

public interface IJwtIssuer
{
    IssuedJwtToken IssueAccessToken(JwtIssueRequest request);
    JsonWebKeySet BuildJwks();
}

public interface IIdentityTokenSigningProvider
{
    TokenSigningSurface GetSurface(SurfaceKind surface);
    JsonWebKeySet BuildJwks();
    JsonWebKeySet BuildJwks(SurfaceKind surface);
}

public sealed class JwtIssuer(IIdentityTokenSigningProvider signingProvider) : IJwtIssuer
{
    private readonly IIdentityTokenSigningProvider _signingProvider = signingProvider;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public IssuedJwtToken IssueAccessToken(JwtIssueRequest request)
    {
        var surfaceConfig = _signingProvider.GetSurface(request.Surface);
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(surfaceConfig.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, request.Subject),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("surface", request.Surface.ToString().ToLowerInvariant()),
        };

        foreach (var claim in request.Claims)
        {
            claims.Add(claim);
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = surfaceConfig.Issuer,
            Audience = surfaceConfig.Audience,
            NotBefore = now.UtcDateTime,
            IssuedAt = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = surfaceConfig.SigningCredentials,
        };

        var token = _tokenHandler.CreateToken(descriptor);
        return new IssuedJwtToken(_tokenHandler.WriteToken(token), expiresAt, surfaceConfig.KeyId);
    }

    public JsonWebKeySet BuildJwks()
    {
        return _signingProvider.BuildJwks();
    }
}

public sealed class IdentityTokenSigningProvider : IIdentityTokenSigningProvider, IHostedService
{
    private const string TestEnvironmentName = "Test";
    private const string DefaultCustomerDevPrivateKeyPath = "infra/dev-keys/identity.customer.ecdsa.private.pem";
    private const string DefaultAdminDevPrivateKeyPath = "infra/dev-keys/identity.admin.ecdsa.private.pem";

    private readonly IOptions<IdentityJwtOptions> _options;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<IdentityTokenSigningProvider> _logger;
    private readonly Dictionary<SurfaceKind, TokenSigningSurface> _surfaces = [];
    private readonly Lock _initializationLock = new();
    private bool _initialized;

    public IdentityTokenSigningProvider(
        IOptions<IdentityJwtOptions> options,
        IHostEnvironment hostEnvironment,
        ILogger<IdentityTokenSigningProvider> logger)
    {
        _options = options;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public TokenSigningSurface GetSurface(SurfaceKind surface)
    {
        EnsureInitialized();
        return _surfaces[surface];
    }

    public JsonWebKeySet BuildJwks()
    {
        EnsureInitialized();
        return BuildJwks(_surfaces.Keys.ToArray());
    }

    public JsonWebKeySet BuildJwks(SurfaceKind surface)
    {
        EnsureInitialized();
        return BuildJwks([surface]);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        EnsureInitialized();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (_initializationLock)
        {
            if (_initialized)
            {
                return;
            }

            var settings = _options.Value;
            var allowDevFallback = _hostEnvironment.IsDevelopment() || _hostEnvironment.IsEnvironment(TestEnvironmentName);
            var contentRootPath = string.IsNullOrWhiteSpace(_hostEnvironment.ContentRootPath)
                ? Directory.GetCurrentDirectory()
                : _hostEnvironment.ContentRootPath;

            _surfaces.Clear();
            _surfaces[SurfaceKind.Customer] = CreateSurface(
                SurfaceKind.Customer,
                settings.Customer,
                fallbackIssuer: "platform-identity",
                fallbackAudience: "customer.api",
                fallbackKid: "customer-current",
                defaultAccessTokenMinutes: 15,
                allowDevFallback,
                contentRootPath);

            _surfaces[SurfaceKind.Admin] = CreateSurface(
                SurfaceKind.Admin,
                settings.Admin,
                fallbackIssuer: "platform-identity",
                fallbackAudience: "admin.api",
                fallbackKid: "admin-current",
                defaultAccessTokenMinutes: 5,
                allowDevFallback,
                contentRootPath);

            _initialized = true;
        }
    }

    private TokenSigningSurface CreateSurface(
        SurfaceKind surface,
        IdentityJwtSurfaceOptions? configured,
        string fallbackIssuer,
        string fallbackAudience,
        string fallbackKid,
        int defaultAccessTokenMinutes,
        bool allowDevFallback,
        string contentRootPath)
    {
        var privateKeyPem = ResolveCurrentPrivateKeyPem(surface, configured, allowDevFallback, contentRootPath);
        var signingEcdsa = ECDsa.Create();
        signingEcdsa.ImportFromPem(privateKeyPem);
        var signingKey = new ECDsaSecurityKey(signingEcdsa)
        {
            KeyId = string.IsNullOrWhiteSpace(configured?.KeyId) ? fallbackKid : configured.KeyId,
        };

        var validationKeys = new List<SecurityKey> { signingKey };
        foreach (var retiredKey in configured?.RetiredValidationKeys ?? [])
        {
            validationKeys.Add(CreateRetiredValidationKey(surface, retiredKey, contentRootPath));
        }

        return new TokenSigningSurface(
            signingKey.KeyId ?? fallbackKid,
            signingKey,
            validationKeys,
            new SigningCredentials(signingKey, SecurityAlgorithms.EcdsaSha256),
            string.IsNullOrWhiteSpace(configured?.Issuer) ? fallbackIssuer : configured.Issuer,
            string.IsNullOrWhiteSpace(configured?.Audience) ? fallbackAudience : configured.Audience,
            configured?.AccessTokenMinutes is > 0 ? configured.AccessTokenMinutes : defaultAccessTokenMinutes);
    }

    private ECDsaSecurityKey CreateRetiredValidationKey(
        SurfaceKind surface,
        IdentityJwtRetiredKeyOptions retiredKey,
        string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(retiredKey.KeyId))
        {
            throw new InvalidOperationException(
                $"Identity retired JWT key id is required for {surface}.");
        }

        var publicKeyPem = ResolveRetiredPublicKeyPem(surface, retiredKey, contentRootPath);
        var retiredEcdsa = ECDsa.Create();
        retiredEcdsa.ImportFromPem(publicKeyPem);

        return new ECDsaSecurityKey(retiredEcdsa)
        {
            KeyId = retiredKey.KeyId,
        };
    }

    private string ResolveCurrentPrivateKeyPem(
        SurfaceKind surface,
        IdentityJwtSurfaceOptions? configured,
        bool allowDevFallback,
        string contentRootPath)
    {
        if (!string.IsNullOrWhiteSpace(configured?.PrivateKeyPem))
        {
            return configured.PrivateKeyPem;
        }

        if (!string.IsNullOrWhiteSpace(configured?.PrivateKeyPath))
        {
            var configuredPath = ResolvePath(configured.PrivateKeyPath, contentRootPath);
            return File.ReadAllText(configuredPath);
        }

        if (!allowDevFallback)
        {
            throw new InvalidOperationException(
                $"Identity JWT private key is required for {surface} in this environment.");
        }

        var fallbackPath = ResolvePath(
            configured?.DevelopmentPrivateKeyPath
            ?? (surface == SurfaceKind.Customer ? DefaultCustomerDevPrivateKeyPath : DefaultAdminDevPrivateKeyPath),
            contentRootPath);

        if (File.Exists(fallbackPath))
        {
            return File.ReadAllText(fallbackPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fallbackPath)!);
        using var generatedEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var generatedPem = generatedEcdsa.ExportECPrivateKeyPem();
        File.WriteAllText(fallbackPath, generatedPem);
        _logger.LogInformation(
            "Generated development identity JWT private key for {Surface} at {Path}.",
            surface,
            fallbackPath);

        return generatedPem;
    }

    private static string ResolveRetiredPublicKeyPem(
        SurfaceKind surface,
        IdentityJwtRetiredKeyOptions retiredKey,
        string contentRootPath)
    {
        if (!string.IsNullOrWhiteSpace(retiredKey.PublicKeyPem))
        {
            return retiredKey.PublicKeyPem;
        }

        if (!string.IsNullOrWhiteSpace(retiredKey.PublicKeyPath))
        {
            var keyPath = ResolvePath(retiredKey.PublicKeyPath, contentRootPath);
            return File.ReadAllText(keyPath);
        }

        throw new InvalidOperationException(
            $"Identity retired JWT public key source is required for {surface} key {retiredKey.KeyId}.");
    }

    private static string ResolvePath(string configuredPath, string contentRootPath)
    {
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));
    }

    private JsonWebKeySet BuildJwks(IEnumerable<SurfaceKind> surfaces)
    {
        var jwks = new JsonWebKeySet();
        var emitted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var surfaceKind in surfaces)
        {
            var surface = _surfaces[surfaceKind];
            foreach (var validationKey in surface.ValidationKeys)
            {
                if (validationKey is not ECDsaSecurityKey ecdsaKey)
                {
                    continue;
                }

                var jwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(ecdsaKey);
                jwk.Kid = validationKey.KeyId;
                jwk.Use = "sig";
                jwk.Alg = SecurityAlgorithms.EcdsaSha256;

                if (!emitted.Add($"{surfaceKind}:{jwk.Kid}"))
                {
                    continue;
                }

                jwks.Keys.Add(jwk);
            }
        }

        return jwks;
    }
}

public sealed record TokenSigningSurface(
    string KeyId,
    SecurityKey SigningKey,
    IReadOnlyCollection<SecurityKey> ValidationKeys,
    SigningCredentials SigningCredentials,
    string Issuer,
    string Audience,
    int AccessTokenMinutes);

public sealed record JwtIssueRequest(
    SurfaceKind Surface,
    string Subject,
    IReadOnlyCollection<Claim> Claims);

public sealed record IssuedJwtToken(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string KeyId);

public sealed class IdentityJwtOptions
{
    public const string SectionName = "Identity:Jwt";

    public IdentityJwtSurfaceOptions Customer { get; init; } = new();
    public IdentityJwtSurfaceOptions Admin { get; init; } = new();
}

public sealed class IdentityJwtSurfaceOptions
{
    public string? PrivateKeyPem { get; init; }
    public string? PrivateKeyPath { get; init; }
    public string? DevelopmentPrivateKeyPath { get; init; }
    public string? Issuer { get; init; }
    public string? Audience { get; init; }
    public string? KeyId { get; init; }
    public int AccessTokenMinutes { get; init; }
    public List<IdentityJwtRetiredKeyOptions> RetiredValidationKeys { get; init; } = [];
}

public sealed class IdentityJwtRetiredKeyOptions
{
    public string? KeyId { get; init; }
    public string? PublicKeyPem { get; init; }
    public string? PublicKeyPath { get; init; }
}
