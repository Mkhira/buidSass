using System.Security.Cryptography;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Checkout.Persistence;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Shared;
using BackendApi.Modules.TaxInvoices.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace TaxInvoices.Tests.Infrastructure;

public sealed class InvoicesTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly string CustomerPrivateKeyPem = CreatePrivateKeyPem();
    private static readonly string AdminPrivateKeyPem = CreatePrivateKeyPem();

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("invoices_test")
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
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            TRUNCATE TABLE
                invoices.subscription_checkpoints,
                invoices.invoices_outbox,
                invoices.invoice_render_jobs,
                invoices.credit_note_lines,
                invoices.credit_notes,
                invoices.invoice_lines,
                invoices.invoices,
                orders.orders_outbox,
                orders.order_state_transitions,
                orders.shipment_lines,
                orders.shipments,
                orders.order_lines,
                orders.orders,
                catalog.products,
                catalog.brands,
                identity.account_roles,
                identity.role_permissions,
                identity.permissions,
                identity.roles,
                identity.sessions,
                identity.accounts,
                public.audit_log_entries
            RESTART IDENTITY CASCADE;
            """;
        await cmd.ExecuteNonQueryAsync();
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
            services.RemoveAll<DbContextOptions<OrdersDbContext>>();
            services.RemoveAll<DbContextOptions<InvoicesDbContext>>();
            services.RemoveAll<NpgsqlDataSource>();

            services.AddSingleton(_ => new NpgsqlDataSourceBuilder(ConnectionString).Build());

            void AddCtx<T>() where T : DbContext =>
                services.AddDbContext<T>((provider, options) =>
                {
                    options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>());
                    options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
                });

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseNpgsql(ConnectionString);
                options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
            });
            AddCtx<IdentityDbContext>();
            AddCtx<CatalogDbContext>();
            AddCtx<PricingDbContext>();
            AddCtx<InventoryDbContext>();
            AddCtx<CartDbContext>();
            AddCtx<CheckoutDbContext>();
            AddCtx<OrdersDbContext>();
            AddCtx<InvoicesDbContext>();
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
        await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<InvoicesDbContext>().Database.MigrateAsync();
    }

    private static string CreatePrivateKeyPem()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return ecdsa.ExportECPrivateKeyPem();
    }
}

[CollectionDefinition("invoices-fixture", DisableParallelization = true)]
public sealed class InvoicesCollection : ICollectionFixture<InvoicesTestFactory> { }
