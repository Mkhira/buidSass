using System.Security.Claims;
using System.Text.Encodings.Web;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Verification.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace Verification.Tests.Contract.Infrastructure;

/// <summary>
/// WebApplicationFactory test harness for spec 020 HTTP contract suites
/// (T044–T049, T104). Mirrors the canonical pattern used by
/// <see cref="Reviews.Tests.Contract.Infrastructure.ReviewsApiFactory"/>:
/// boot the full app via <c>WebApplicationFactory&lt;Program&gt;</c>, swap
/// the JWT bearer schemes for a stub auth handler so tests synthesize claims
/// via custom HTTP headers, and migrate the central + verification DbContexts.
///
/// <para>The reference-data seeder is invoked at boot so KSA + EG market
/// schemas are present — every customer-side endpoint requires an active
/// schema row to validate against.</para>
/// </summary>
public sealed class VerificationApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_contract_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        ConnectionString = _postgres.GetConnectionString();
        _ = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
        });
        await EnsureMigrationsAsync();
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    public VerificationDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<VerificationDbContext>()
            .UseNpgsql(ConnectionString)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new VerificationDbContext(options);
    }

    /// <summary>
    /// Truncates every verification-owned table so per-test fixtures start
    /// from a known state. Reference data (market schemas) is preserved —
    /// the seeder runs once at <see cref="EnsureMigrationsAsync"/>.
    /// </summary>
    public async Task ResetVerificationAsync()
    {
        await using var ctx = NewDbContext();
        await ctx.Database.ExecuteSqlRawAsync(
            @"TRUNCATE
                verification.verification_documents,
                verification.verification_state_transitions,
                verification.verification_reminders,
                verification.verifications,
                verification.verification_eligibility_cache
              RESTART IDENTITY CASCADE;");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                ["Identity:Jwt:Customer:Issuer"] = "platform-identity",
                ["Identity:Jwt:Customer:Audience"] = "customer.api",
                ["Identity:Jwt:Customer:KeyId"] = "test-customer-current",
                ["Identity:Jwt:Admin:Issuer"] = "platform-identity",
                ["Identity:Jwt:Admin:Audience"] = "admin.api",
                ["Identity:Jwt:Admin:KeyId"] = "test-admin-current",
                ["Seeding:Enabled"] = "false",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace the Identity-issued JWT bearer schemes with a stub that
            // reads claims from per-scheme headers. Keeps the test surface
            // focused on what verification endpoints consume from the principal
            // without standing up the Identity-side token-issuance pipeline.
            services.RemoveAll<IConfigureOptions<AuthenticationOptions>>();
            services.AddAuthentication("Stub")
                .AddScheme<VerificationStubAuthOptions, VerificationStubJwtHandler>("CustomerJwt",
                    o => o.SchemeHeader = "X-Test-Customer-Id")
                .AddScheme<VerificationStubAuthOptions, VerificationStubJwtHandler>("AdminJwt",
                    o => o.SchemeHeader = "X-Test-Admin-Id");

            services.AddDbContext<VerificationDbContext>((_, options) =>
            {
                options.UseNpgsql(ConnectionString);
                options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
            });
        });
    }

    private async Task EnsureMigrationsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<VerificationDbContext>().Database.MigrateAsync();

        // Reference-data seeder: KSA + EG market schemas. Every customer-side
        // endpoint joins the verification row to its active schema, so no
        // test can pass without these rows.
        var seeder = new BackendApi.Modules.Verification.Seeding.VerificationReferenceDataSeeder();
        var seedCtx = new BackendApi.Features.Seeding.SeedContext(
            null!,
            scope.ServiceProvider,
            BackendApi.Features.Seeding.Datasets.DatasetSize.Small,
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        await seeder.ApplyAsync(seedCtx, CancellationToken.None);
    }
}

public sealed class VerificationStubAuthOptions : AuthenticationSchemeOptions
{
    public string SchemeHeader { get; set; } = "X-Test-Subject";
}

/// <summary>
/// Trivial auth handler used by spec 020 contract tests. Reads:
/// <list type="bullet">
///   <item><c>{SchemeHeader}</c> → actor id (sub + nameidentifier claims).</item>
///   <item><c>X-Test-Market</c> → market_code claim (defaults to KSA per
///         <see cref="BackendApi.Modules.Verification.Customer.VerificationResponseFactory.ResolveMarketCode"/>).</item>
///   <item><c>X-Test-Permissions</c> → CSV permission list, emitted as both
///         <c>permission</c> and <c>permissions</c> claim shapes since the
///         admin slice's revoke endpoint accepts either form.</item>
/// </list>
/// </summary>
public sealed class VerificationStubJwtHandler : AuthenticationHandler<VerificationStubAuthOptions>
{
    public VerificationStubJwtHandler(
        IOptionsMonitor<VerificationStubAuthOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var subjectHeader = Options.SchemeHeader;
        if (!Request.Headers.TryGetValue(subjectHeader, out var subjectValues)
            || subjectValues.Count == 0
            || !Guid.TryParse(subjectValues[0], out var actorId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new("sub", actorId.ToString()),
            new(ClaimTypes.NameIdentifier, actorId.ToString()),
        };
        if (Request.Headers.TryGetValue("X-Test-Market", out var market) && market.Count > 0)
        {
            claims.Add(new Claim("market_code", market[0]!));
        }
        if (Request.Headers.TryGetValue("X-Test-Permissions", out var perms) && perms.Count > 0)
        {
            foreach (var p in perms[0]!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                claims.Add(new Claim("permission", p));
                claims.Add(new Claim("permissions", p));
            }
        }
        if (Request.Headers.TryGetValue("X-Test-Locale", out var locale) && locale.Count > 0)
        {
            claims.Add(new Claim("locale", locale[0]!));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
