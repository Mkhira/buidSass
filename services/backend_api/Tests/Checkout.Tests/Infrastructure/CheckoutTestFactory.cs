using System.Security.Cryptography;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Checkout.Persistence;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Caches;
using BackendApi.Modules.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Checkout.Tests.Infrastructure;

public sealed class CheckoutTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly string CustomerPrivateKeyPem = CreatePrivateKeyPem();
    private static readonly string AdminPrivateKeyPem = CreatePrivateKeyPem();

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("checkout_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCommand("-c", "max_connections=300")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        ConnectionString = $"{_postgres.GetConnectionString()};Maximum Pool Size=300";
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

    public async Task ResetDatabaseAsync()
    {
        Services.GetRequiredService<TaxRateCache>().InvalidateAll();
        Services.GetRequiredService<PromotionCache>().Invalidate();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            TRUNCATE TABLE
                checkout.idempotency_results,
                checkout.payment_webhook_events,
                checkout.shipping_quotes,
                checkout.payment_attempts,
                checkout.sessions,
                cart.cart_abandoned_emissions,
                cart.cart_b2b_metadata,
                cart.cart_saved_items,
                cart.cart_lines,
                cart.carts,
                inventory.reorder_alert_debounce,
                inventory.inventory_movements,
                inventory.inventory_reservations,
                inventory.inventory_batches,
                inventory.stock_levels,
                inventory.warehouses,
                pricing.price_explanations,
                pricing.coupon_redemptions,
                pricing.coupons,
                pricing.promotions,
                pricing.product_tier_prices,
                pricing.account_b2b_tiers,
                pricing.b2b_tiers,
                pricing.tax_rates,
                pricing.bundle_memberships,
                catalog.catalog_outbox,
                catalog.bulk_import_idempotency,
                catalog.scheduled_publishes,
                catalog.product_state_transitions,
                catalog.product_documents,
                catalog.product_media,
                catalog.product_categories,
                catalog.products,
                catalog.manufacturers,
                catalog.brands,
                catalog.category_attribute_schemas,
                catalog.category_closure,
                catalog.categories,
                identity.account_roles,
                identity.role_permissions,
                identity.permissions,
                identity.roles,
                identity.authorization_audit,
                identity.rate_limit_events,
                identity.admin_mfa_replay_guard,
                identity.admin_mfa_factors,
                identity.admin_mfa_challenges,
                identity.admin_partial_auth_tokens,
                identity.admin_invitations,
                identity.password_reset_tokens,
                identity.email_verification_challenges,
                identity.otp_challenges,
                identity.revoked_refresh_tokens,
                identity.refresh_tokens,
                identity.sessions,
                identity.lockout_state,
                identity.accounts,
                public.audit_log_entries,
                public.seed_applied
            RESTART IDENTITY CASCADE;
            """;
        await command.ExecuteNonQueryAsync();
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
                ["Identity:Jwt:Customer:PrivateKeyPem"] = CustomerPrivateKeyPem,
                ["Identity:Jwt:Customer:KeyId"] = "test-customer-current",
                ["Identity:Jwt:Admin:Issuer"] = "platform-identity",
                ["Identity:Jwt:Admin:Audience"] = "admin.api",
                ["Identity:Jwt:Admin:PrivateKeyPem"] = AdminPrivateKeyPem,
                ["Identity:Jwt:Admin:KeyId"] = "test-admin-current",
                ["Inventory:ReservationTtlMinutes"] = "15",
                ["Cart:TokenSecret"] = "test-cart-token-secret-0123456789abcdef",
                ["Seeding:Enabled"] = "false",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptions<IdentityDbContext>>();
            services.RemoveAll<DbContextOptions<CatalogDbContext>>();
            services.RemoveAll<DbContextOptions<PricingDbContext>>();
            services.RemoveAll<DbContextOptions<InventoryDbContext>>();
            services.RemoveAll<DbContextOptions<CartDbContext>>();
            services.RemoveAll<DbContextOptions<CheckoutDbContext>>();
            services.RemoveAll<NpgsqlDataSource>();

            services.AddSingleton(_ => new NpgsqlDataSourceBuilder(ConnectionString).Build());

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseNpgsql(ConnectionString);
                options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
            });
            services.AddDbContext<IdentityDbContext>((provider, options) =>
            {
                options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>());
                options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
            });
            services.AddDbContext<CatalogDbContext>((provider, options) =>
            {
                options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>());
                options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
            });
            services.AddDbContext<PricingDbContext>((provider, options) =>
            {
                options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>());
                options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
            });
            services.AddDbContext<InventoryDbContext>((provider, options) =>
            {
                options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>());
                options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
            });
            services.AddDbContext<CartDbContext>((provider, options) =>
            {
                options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>());
                options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
            });
            services.AddDbContext<CheckoutDbContext>((provider, options) =>
            {
                options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>());
                options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
            });
        });
    }

    private async Task EnsureMigrationsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<PricingDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<CartDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<CheckoutDbContext>().Database.MigrateAsync();
    }

    private static string CreatePrivateKeyPem()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return ecdsa.ExportECPrivateKeyPem();
    }
}
