using System.Security.Claims;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Shared.Testing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using System.Text.Encodings.Web;
using Testcontainers.PostgreSql;

namespace Reviews.Tests.Contract.Infrastructure;

/// <summary>
/// WebApplicationFactory test harness for spec 022 HTTP contract suites.
/// Spins up a Testcontainers Postgres + boots the full app via
/// <c>WebApplicationFactory&lt;Program&gt;</c>; swaps in a stub auth scheme
/// (<see cref="StubJwtHandler"/>) so tests can synthesize JWT-style claims
/// via custom HTTP headers without needing the Identity-side token-issuance
/// pipeline. The Reviews module's own DbContext + the central
/// <see cref="AppDbContext"/> are migrated; sibling modules' schemas are
/// not, since the Reviews HTTP surface only depends on its own schema +
/// the central one.
/// </summary>
public sealed class ReviewsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("reviews_contract_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public FakeReviewDomainEventCollector EventCollector { get; } = new();

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
            // Stub the auth schemes Reviews endpoints require so contract
            // tests don't need to mint real signed JWTs.
            services.RemoveAll<IConfigureOptions<AuthenticationOptions>>();
            services.AddAuthentication("Stub")
                .AddScheme<StubAuthOptions, StubJwtHandler>("CustomerJwt",
                    o => o.SchemeHeader = "X-Test-Customer-Id")
                .AddScheme<StubAuthOptions, StubJwtHandler>("AdminJwt",
                    o => o.SchemeHeader = "X-Test-Admin-Id");

            // Swap the cross-module fakes for deterministic test data.
            services.RemoveAll<IOrderLineDeliveryEligibilityQuery>();
            services.AddScoped<IOrderLineDeliveryEligibilityQuery>(_ =>
                new FakeOrderLineDeliveryEligibilityQuery(
                    eligible: true,
                    deliveredAt: DateTimeOffset.UtcNow.AddDays(-2),
                    orderLineId: Guid.NewGuid()));
            services.RemoveAll<IReviewReporterFactsQuery>();
            services.AddScoped<IReviewReporterFactsQuery>(_ => FakeReviewReporterFactsQuery.Qualified);

            // Capture domain events for contract-test assertions.
            services.RemoveAll<IReviewDomainEventPublisher>();
            services.AddSingleton<IReviewDomainEventPublisher>(EventCollector);

            // Deterministic clock for any time-based assertions.
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow));

            services.AddDbContext<ReviewsDbContext>((_, options) =>
            {
                options.UseNpgsql(ConnectionString);
                options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
            });
        });
    }

    private async Task EnsureMigrationsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        // Central app DbContext migrates infra schemas (audit, etc.).
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<ReviewsDbContext>().Database.MigrateAsync();

        // Reviews seeder — KSA + EG market schemas + base wordlist so
        // submission paths have a working policy resolver.
        var seeder = new BackendApi.Modules.Reviews.Seeding.ReviewsReferenceDataSeeder();
        var seedCtx = new BackendApi.Features.Seeding.SeedContext(
            null!,
            scope.ServiceProvider,
            BackendApi.Features.Seeding.Datasets.DatasetSize.Small,
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        await seeder.ApplyAsync(seedCtx, CancellationToken.None);
    }
}

public sealed class StubAuthOptions : AuthenticationSchemeOptions
{
    public string SchemeHeader { get; set; } = "X-Test-Subject";
}

/// <summary>
/// Trivial auth scheme used by contract tests. Reads the per-scheme header
/// for the actor id; consults <c>X-Test-Permissions</c> for a CSV permission
/// list; <c>X-Test-Market</c> for the market_code claim. Keeps the test
/// surface focused on what spec 022 endpoints actually consume from claims
/// without standing up the Identity-side token pipeline.
/// </summary>
public sealed class StubJwtHandler : AuthenticationHandler<StubAuthOptions>
{
    public StubJwtHandler(
        IOptionsMonitor<StubAuthOptions> options,
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
            }
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
