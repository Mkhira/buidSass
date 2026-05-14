using System.Security.Claims;
using System.Text.Encodings.Web;
using BackendApi.Features.Seeding;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Shared.Testing;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Seeding;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace Support.Tests.Contract.Infrastructure;

/// <summary>
/// WebApplicationFactory test harness for the spec 023 Support HTTP contract
/// suite (tasks T053 / T066 / T075 / T083 / T092 / T101). Mirrors the
/// <c>ReviewsApiFactory</c> pattern from spec 022:
///
///   1. Spins up a Testcontainers Postgres instance.
///   2. Boots the full app via <c>WebApplicationFactory&lt;Program&gt;</c>.
///   3. Stubs the <c>CustomerJwt</c> + <c>AdminJwt</c> auth schemes so tests
///      can synthesize JWT-style claims via custom HTTP headers without
///      standing up the Identity-side token-issuance pipeline.
///   4. Swaps every cross-module read / write contract for the in-memory
///      fakes under <c>BackendApi.Modules.Shared.Testing</c>. Spec 023's
///      module registers <c>TryAdd</c> null bindings, so we
///      <c>RemoveAll</c> + add the fakes here.
///   5. Migrates the central <c>AppDbContext</c> + the per-spec
///      <c>SupportDbContext</c>, then runs the reference-data seeder so the
///      KSA + EG market schemas + 8 SLA policies are in place.
///
/// Per-test isolation is provided by truncating <c>support.*</c> tables in
/// the factory's exposed <see cref="ResetAsync"/> helper (the reference-data
/// rows are preserved; per-test rows are wiped).
/// </summary>
public sealed class SupportApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("support_contract_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Captured cross-module fakes that tests pre-stage data against.</summary>
    public FakeOrderLinkedReadContract OrderContract { get; } = new();

    public FakeReturnLinkedReadContract ReturnContract { get; } = new();

    public FakeQuoteLinkedReadContract QuoteContract { get; } = new();

    public FakeReviewLinkedReadContract ReviewContract { get; } = new();

    public FakeVerificationLinkedReadContract VerificationContract { get; } = new();

    public FakeCompanyAccountQuery CompanyAccountQuery { get; } = new();

    public FakeReturnRequestCreationContract ReturnCreationContract { get; } = new();

    public FakeReviewDisplayHandleQuery ReviewDisplayHandleQuery { get; } = FakeReviewDisplayHandleQuery.Empty;

    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-05-06T12:00:00+00:00"));

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        ConnectionString = _postgres.GetConnectionString();

        // Touching CreateClient forces the host pipeline to boot, exposing any
        // config / DI failures up front rather than inside the first test method.
        _ = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
        });

        await EnsureMigrationsAndSeedAsync();
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
            // -- Stub auth so tests can synthesize claims via headers. Replaces
            //    the JwtBearer pipeline wired by IdentityModule for the test host only.
            services.RemoveAll<IConfigureOptions<AuthenticationOptions>>();
            services.AddAuthentication("Stub")
                .AddScheme<StubAuthOptions, StubJwtHandler>("CustomerJwt",
                    o => o.SchemeHeader = "X-Test-Customer-Id")
                .AddScheme<StubAuthOptions, StubJwtHandler>("AdminJwt",
                    o => o.SchemeHeader = "X-Test-Admin-Id");

            // -- Cross-module fakes. SupportModule.cs registers Null* fallbacks
            //    via TryAdd; we replace those with capturing fakes so tests can
            //    pre-stage linked-entity rows + observe outgoing contract calls.
            services.RemoveAll<IOrderLinkedReadContract>();
            services.AddScoped<IOrderLinkedReadContract>(_ => OrderContract);

            services.RemoveAll<IReturnLinkedReadContract>();
            services.AddScoped<IReturnLinkedReadContract>(_ => ReturnContract);

            services.RemoveAll<IQuoteLinkedReadContract>();
            services.AddScoped<IQuoteLinkedReadContract>(_ => QuoteContract);

            services.RemoveAll<IReviewLinkedReadContract>();
            services.AddScoped<IReviewLinkedReadContract>(_ => ReviewContract);

            services.RemoveAll<IVerificationLinkedReadContract>();
            services.AddScoped<IVerificationLinkedReadContract>(_ => VerificationContract);

            services.RemoveAll<ICompanyAccountQuery>();
            services.AddScoped<ICompanyAccountQuery>(_ => CompanyAccountQuery);

            services.RemoveAll<IReturnRequestCreationContract>();
            services.AddScoped<IReturnRequestCreationContract>(_ => ReturnCreationContract);

            services.RemoveAll<IReviewDisplayHandleQuery>();
            services.AddScoped<IReviewDisplayHandleQuery>(_ => ReviewDisplayHandleQuery);

            // Deterministic clock for SLA-deadline assertions.
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            services.AddDbContext<SupportDbContext>((_, options) =>
            {
                options.UseNpgsql(ConnectionString);
                options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
            });
        });
    }

    private async Task EnsureMigrationsAndSeedAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SupportDbContext>().Database.MigrateAsync();

        var seeder = new SupportReferenceDataSeeder();
        var seedCtx = new SeedContext(
            Db: null!,
            Services: scope.ServiceProvider,
            Size: BackendApi.Features.Seeding.Datasets.DatasetSize.Small,
            Env: scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>(),
            Logger: NullLogger.Instance);
        await seeder.ApplyAsync(seedCtx, CancellationToken.None);
    }

    /// <summary>
    /// Truncate per-ticket rows between tests while preserving the reference
    /// data (market schemas + SLA policies). Tests that need cross-test
    /// isolation call this from their setup.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE support.ticket_sla_breach_events, " +
            "support.ticket_messages, support.ticket_attachments, " +
            "support.ticket_links, support.ticket_assignments, " +
            "support.tickets RESTART IDENTITY CASCADE;");
    }
}

public sealed class StubAuthOptions : AuthenticationSchemeOptions
{
    public string SchemeHeader { get; set; } = "X-Test-Subject";
}

/// <summary>
/// Trivial auth scheme used by contract tests. Reads the per-scheme header
/// for the actor id; consults <c>X-Test-Permissions</c> for a CSV permission
/// list; <c>X-Test-Market</c> for the market_code claim.
/// </summary>
public sealed class StubJwtHandler : AuthenticationHandler<StubAuthOptions>
{
    public StubJwtHandler(
        IOptionsMonitor<StubAuthOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

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
            foreach (var p in perms[0]!.Split(',',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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

[CollectionDefinition(nameof(SupportApiCollection))]
public sealed class SupportApiCollection : ICollectionFixture<SupportApiFactory>
{
}
