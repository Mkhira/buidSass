using System.Security.Claims;
using System.Text.Encodings.Web;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Seeding;
using BackendApi.Modules.Shared;
using B2B.Tests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace B2B.Tests.Contract.Infrastructure;

/// <summary>
/// Spec 021 Phase 3 (US1) — HTTP contract test harness. Mirrors the
/// <c>ReviewsApiFactory</c> pattern (spec 022): boots a Testcontainers Postgres,
/// applies the <see cref="AppDbContext"/> + <see cref="B2BDbContext"/> migrations,
/// runs the <see cref="B2BReferenceDataSeeder"/> for KSA + EG market schemas, then
/// boots the full ASP.NET app via <see cref="WebApplicationFactory{TEntryPoint}"/>.
///
/// Auth schemes are stubbed via <see cref="StubJwtHandler"/> so contract tests can
/// synthesize the same claims a real customer JWT would carry (subject, market_code,
/// permissions) without standing up the Identity-side token-issuance pipeline.
/// Cross-module dependency hooks (<see cref="ICartSnapshotProvider"/>,
/// <see cref="IProductRestrictionPolicy"/>, <see cref="IProductCatalogQuery"/>,
/// <see cref="ICustomerVerificationEligibilityQuery"/>,
/// <see cref="IPricingBaselineProvider"/>, <see cref="IOrderFromQuoteHandler"/>) are
/// registered as test stubs from <c>B2B.Tests.Fixtures</c>.
///
/// Phase 3 caveat: User Story 1 endpoints are not yet mapped in
/// <see cref="BackendApi.Modules.B2B.B2BModule.UseB2BModuleEndpoints"/>; every contract
/// test in this Cycle therefore expects the canonical TDD-red signal of
/// <c>404 Not Found</c> against the requested route until the matching handler lands
/// in Cycle B. The harness itself is fully functional and reusable across cycles.
/// </summary>
public sealed class B2BApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("b2b_contract_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// Captures every <see cref="MediatR.INotification"/> the request pipeline emits.
    /// US1 happy-path tests assert that <c>QuoteRequested</c> shows up exactly once.
    /// </summary>
    public FakeQuoteDomainEventCollector EventCollector { get; } = new();

    public StubCartSnapshotProvider CartSnapshotProvider { get; } = new();
    public StubProductRestrictionPolicy ProductRestrictionPolicy { get; } = new();
    public StubProductCatalogQuery ProductCatalogQuery { get; } = new();
    public StubCustomerVerificationEligibilityQuery VerificationEligibilityQuery { get; } = new();
    public StubPricingBaselineProvider PricingBaselineProvider { get; } = new();
    public StubOrderFromQuoteHandler OrderFromQuoteHandler { get; } = new();

    /// <summary>
    /// Test-controlled clock. Cycle B integration tests (rate-limit windows, expiry
    /// workers, validity-extension on publish) advance this with
    /// <see cref="FakeTimeProvider.Advance(TimeSpan)"/> instead of relying on real time.
    /// </summary>
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    /// <summary>
    /// Resets every shared stub's mutable state. xUnit shares the class-fixture across
    /// every <c>[Fact]</c> in a class, so any test that seeds
    /// <c>CartSnapshotProvider.SnapshotsByCustomer</c>, expects empty
    /// <c>EventCollector</c>, or asserts on <c>OrderFromQuoteHandler.Invocations</c> MUST
    /// call this first to avoid bleed-through from prior tests.
    /// </summary>
    public void ResetStubState()
    {
        CartSnapshotProvider.SnapshotsByCustomer.Clear();
        CartSnapshotProvider.ClearedCustomerIds.Clear();
        ProductRestrictionPolicy.PolicyBySku.Clear();
        ProductRestrictionPolicy.Invocations.Clear();
        ProductCatalogQuery.InactiveSkus.Clear();
        ProductCatalogQuery.NonQuotableProductIds.Clear();
        VerificationEligibilityQuery.ResultBySkuAndCustomer.Clear();
        VerificationEligibilityQuery.SingleInvocations.Clear();
        VerificationEligibilityQuery.BatchInvocations.Clear();
        PricingBaselineProvider.OverridesBySku.Clear();
        OrderFromQuoteHandler.ReplayMap.Clear();
        OrderFromQuoteHandler.Invocations.Clear();
        OrderFromQuoteHandler.FailureToThrow = null;
        EventCollector.Clear();
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        ConnectionString = _postgres.GetConnectionString();
        // Force the host to spin up so ConfigureWebHost runs before EnsureMigrations.
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
                // B2B:Invitation:SigningKey — bound by B2BInvitationOptions
                // (Modules/B2B/Primitives/B2BInvitationOptions.cs, SectionName="B2B:Invitation").
                // Cycle A tests don't exercise the invitation path so the hasher is never
                // resolved; Cycle B's invitation suite WILL resolve it, and supplying the
                // key here keeps that future test green without re-touching this fixture.
                // The hasher accepts either a Base64 key or a raw UTF-8 string ≥ 32 bytes;
                // this 39-char ASCII string contains '-' (not Base64-valid) so the hasher
                // falls back to UTF-8 decoding — 39 bytes ≥ 32 satisfies the entropy floor.
                ["B2B:Invitation:SigningKey"] = "test-current-signing-key-256-bits-padding",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Stub auth: customer + admin JWT schemes resolve from per-scheme headers
            // instead of validating real signed tokens. Removes the Identity-token
            // dependency from the contract surface.
            services.RemoveAll<IConfigureOptions<AuthenticationOptions>>();
            services.AddAuthentication("Stub")
                .AddScheme<StubAuthOptions, StubJwtHandler>("CustomerJwt",
                    o => o.SchemeHeader = "X-Test-Customer-Id")
                .AddScheme<StubAuthOptions, StubJwtHandler>("AdminJwt",
                    o => o.SchemeHeader = "X-Test-Admin-Id");

            // Cross-module hook stubs — every spec 021 dependency declared in
            // Modules/Shared/ is replaced with a deterministic test double.
            services.RemoveAll<ICartSnapshotProvider>();
            services.AddSingleton<ICartSnapshotProvider>(CartSnapshotProvider);

            services.RemoveAll<IProductRestrictionPolicy>();
            services.AddSingleton<IProductRestrictionPolicy>(ProductRestrictionPolicy);

            services.RemoveAll<IProductCatalogQuery>();
            services.AddSingleton<IProductCatalogQuery>(ProductCatalogQuery);

            services.RemoveAll<ICustomerVerificationEligibilityQuery>();
            services.AddSingleton<ICustomerVerificationEligibilityQuery>(VerificationEligibilityQuery);

            services.RemoveAll<IPricingBaselineProvider>();
            services.AddSingleton<IPricingBaselineProvider>(PricingBaselineProvider);

            services.RemoveAll<IOrderFromQuoteHandler>();
            services.AddSingleton<IOrderFromQuoteHandler>(OrderFromQuoteHandler);

            // Capture every domain event the pipeline emits so US1 tests can assert
            // QuoteRequested fan-out without coupling to a real notification subscriber.
            services.AddSingleton(EventCollector);
            services.AddTransient<MediatR.INotificationHandler<QuoteRequested>>(
                sp => sp.GetRequiredService<FakeQuoteDomainEventCollector>());
            services.AddTransient<MediatR.INotificationHandler<QuotePublished>>(
                sp => sp.GetRequiredService<FakeQuoteDomainEventCollector>());
            services.AddTransient<MediatR.INotificationHandler<QuotePendingApprover>>(
                sp => sp.GetRequiredService<FakeQuoteDomainEventCollector>());
            services.AddTransient<MediatR.INotificationHandler<QuoteAccepted>>(
                sp => sp.GetRequiredService<FakeQuoteDomainEventCollector>());
            services.AddTransient<MediatR.INotificationHandler<QuoteRejected>>(
                sp => sp.GetRequiredService<FakeQuoteDomainEventCollector>());
            services.AddTransient<MediatR.INotificationHandler<QuoteApproverRejected>>(
                sp => sp.GetRequiredService<FakeQuoteDomainEventCollector>());
            services.AddTransient<MediatR.INotificationHandler<QuoteExpired>>(
                sp => sp.GetRequiredService<FakeQuoteDomainEventCollector>());
            services.AddTransient<MediatR.INotificationHandler<QuoteWithdrawn>>(
                sp => sp.GetRequiredService<FakeQuoteDomainEventCollector>());

            // Deterministic clock for any time-based assertions (rate-limit windows,
            // expiry checks). Tests advance via factory.Time.Advance(...) — same instance
            // is registered as the host's TimeProvider so handler-resolved
            // TimeProvider.GetUtcNow() observes the advanced value.
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);

            // The B2BModule already suppresses ManyServiceProvidersCreatedWarning per
            // project-memory rule; re-registering DbContext here is unnecessary unless
            // the test container connection string differs — which it does. We rebind
            // it to point at the Testcontainers instance.
            services.AddDbContext<B2BDbContext>((_, options) =>
            {
                options.UseNpgsql(ConnectionString);
                options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
            });
        });
    }

    private async Task EnsureMigrationsAsync()
    {
        await using var scope = Services.CreateAsyncScope();

        // Central app DbContext owns infra schemas (audit_log, etc.) that B2B writes into.
        var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await appDb.Database.MigrateAsync();

        var b2bDb = scope.ServiceProvider.GetRequiredService<B2BDbContext>();
        await b2bDb.Database.MigrateAsync();

        // Seed KSA + EG market schemas so any path needing QuoteMarketPolicy resolves.
        var seeder = new B2BReferenceDataSeeder();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var seedCtx = new BackendApi.Features.Seeding.SeedContext(
            Db: appDb,
            Services: scope.ServiceProvider,
            Size: BackendApi.Features.Seeding.Datasets.DatasetSize.Small,
            Env: env,
            Logger: NullLogger.Instance);
        await seeder.ApplyAsync(seedCtx, CancellationToken.None);
    }
}

/// <summary>
/// Options for the test-only auth scheme. The single configurable surface is
/// <see cref="SchemeHeader"/> — the HTTP request header that carries the actor id.
/// One scheme instance is registered per role (customer, admin) with distinct headers.
/// </summary>
public sealed class StubAuthOptions : AuthenticationSchemeOptions
{
    public string SchemeHeader { get; set; } = "X-Test-Subject";
}

/// <summary>
/// Test-only auth scheme that bypasses real JWT validation. Reads the per-scheme
/// header for the actor id and consults <c>X-Test-Market</c> +
/// <c>X-Test-Permissions</c> for additional claims. Mirrors the
/// <c>StubJwtHandler</c> in <c>Reviews.Tests.Contract.Infrastructure</c> — kept as a
/// per-module sibling rather than promoted to <c>Modules/Shared/Testing</c> because
/// each module's contract surface chooses its own header conventions.
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
