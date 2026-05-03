using System.Security.Claims;
using System.Text.Encodings.Web;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Shared;
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

namespace Cms.Tests.Contract.Infrastructure;

/// <summary>
/// WebApplicationFactory test harness for spec 024 HTTP contract suites.
/// Boots the full app via WebApplicationFactory&lt;Program&gt;, swaps in the
/// shared StubJwtHandler so tests synthesize JWT-like claims via custom
/// headers, and migrates the central + cms schemas. Mirrors the pattern
/// used by ReviewsApiFactory (spec 022).
/// </summary>
public sealed class CmsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("cms_contract_test")
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

    public CmsDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<CmsDbContext>()
            .UseNpgsql(ConnectionString)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new CmsDbContext(options);
    }

    public async Task ResetCmsAsync()
    {
        await using var ctx = NewDbContext();
        await ctx.Database.ExecuteSqlRawAsync(
            @"TRUNCATE
                cms.banner_campaign_bindings,
                cms.preview_tokens,
                cms.assets,
                cms.banner_slots,
                cms.featured_sections,
                cms.faq_entries,
                cms.blog_articles,
                cms.legal_page_versions
              RESTART IDENTITY CASCADE;");

        // FakeCatalog* are registered as singletons (so their stubbed product /
        // category / bundle dictionaries persist across the request pipeline
        // within one test). Clear them between tests so per-test setup
        // doesn't accumulate phantom entries from earlier cases — without
        // this, performance tests priming 10k products would leak into
        // subsequent contract assertions.
        ((BackendApi.Modules.Shared.Testing.FakeCatalogProductReadContract)
            Services.GetRequiredService<BackendApi.Modules.Shared.ICatalogProductReadContract>()).Clear();
        ((BackendApi.Modules.Shared.Testing.FakeCatalogCategoryReadContract)
            Services.GetRequiredService<BackendApi.Modules.Shared.ICatalogCategoryReadContract>()).Clear();
        ((BackendApi.Modules.Shared.Testing.FakeCatalogBundleReadContract)
            Services.GetRequiredService<BackendApi.Modules.Shared.ICatalogBundleReadContract>()).Clear();
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
                ["Cms:Preview:HmacKey"] = "test-cms-preview-hmac-key-32-bytes-padded!!",
                ["Seeding:Enabled"] = "false",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Stub auth scheme so contract tests synthesize claims via headers.
            services.RemoveAll<IConfigureOptions<AuthenticationOptions>>();
            services.AddAuthentication("Stub")
                .AddScheme<CmsStubAuthOptions, CmsStubJwtHandler>("CustomerJwt",
                    o => o.SchemeHeader = "X-Test-Customer-Id")
                .AddScheme<CmsStubAuthOptions, CmsStubJwtHandler>("AdminJwt",
                    o => o.SchemeHeader = "X-Test-Admin-Id");

            services.AddDbContext<CmsDbContext>((_, options) =>
            {
                options.UseNpgsql(ConnectionString);
                options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
            });

            // Replace the production NullCatalog* fallbacks with the
            // FakeCatalog* stubs so storefront / admin reads that resolve
            // refs against the catalog return "available" by default in
            // contract tests.
            services.RemoveAll<BackendApi.Modules.Shared.ICatalogProductReadContract>();
            services.RemoveAll<BackendApi.Modules.Shared.ICatalogCategoryReadContract>();
            services.RemoveAll<BackendApi.Modules.Shared.ICatalogBundleReadContract>();
            services.AddSingleton<BackendApi.Modules.Shared.ICatalogProductReadContract,
                BackendApi.Modules.Shared.Testing.FakeCatalogProductReadContract>();
            services.AddSingleton<BackendApi.Modules.Shared.ICatalogCategoryReadContract,
                BackendApi.Modules.Shared.Testing.FakeCatalogCategoryReadContract>();
            services.AddSingleton<BackendApi.Modules.Shared.ICatalogBundleReadContract,
                BackendApi.Modules.Shared.Testing.FakeCatalogBundleReadContract>();
        });
    }

    private async Task EnsureMigrationsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<CmsDbContext>().Database.MigrateAsync();

        // Seed CMS reference data (3 market schemas).
        var refSeeder = new BackendApi.Modules.Cms.Seeding.CmsReferenceDataSeeder();
        var seedCtx = new BackendApi.Features.Seeding.SeedContext(
            null!,
            scope.ServiceProvider,
            BackendApi.Features.Seeding.Datasets.DatasetSize.Small,
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        await refSeeder.ApplyAsync(seedCtx, CancellationToken.None);
    }
}

public sealed class CmsStubAuthOptions : AuthenticationSchemeOptions
{
    public string SchemeHeader { get; set; } = "X-Test-Subject";
}

public sealed class CmsStubJwtHandler : AuthenticationHandler<CmsStubAuthOptions>
{
    public CmsStubJwtHandler(
        IOptionsMonitor<CmsStubAuthOptions> options,
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
        if (Request.Headers.TryGetValue("X-Test-Role", out var role) && role.Count > 0)
        {
            claims.Add(new Claim("role", role[0]!));
            claims.Add(new Claim(ClaimTypes.Role, role[0]!));
        }
        if (Request.Headers.TryGetValue("X-Test-Permissions", out var perms) && perms.Count > 0)
        {
            foreach (var p in perms[0]!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                claims.Add(new Claim("permissions", p));
            }
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
