using System.Net;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using System.Security.Cryptography;
using Testcontainers.PostgreSql;

namespace Identity.Tests.Infrastructure;

public sealed class IdentityTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly string CustomerPrivateKeyPem = CreatePrivateKeyPem();
    private static readonly string AdminPrivateKeyPem = CreatePrivateKeyPem();

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("identity_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

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
        Services.GetRequiredService<IdentityDispatchCaptureStore>().Clear();

        var connectionString = _postgres.GetConnectionString();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            TRUNCATE TABLE
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
                identity.accounts
            RESTART IDENTITY CASCADE;

            TRUNCATE TABLE
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
            var overrides = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                ["Identity:Jwt:Customer:Issuer"] = "platform-identity",
                ["Identity:Jwt:Customer:Audience"] = "customer.api",
                ["Identity:Jwt:Customer:PrivateKeyPem"] = CustomerPrivateKeyPem,
                ["Identity:Jwt:Customer:KeyId"] = "test-customer-current",
                ["Identity:Jwt:Admin:Issuer"] = "platform-identity",
                ["Identity:Jwt:Admin:Audience"] = "admin.api",
                ["Identity:Jwt:Admin:PrivateKeyPem"] = AdminPrivateKeyPem,
                ["Identity:Jwt:Admin:KeyId"] = "test-admin-current",
                ["Seeding:Enabled"] = "false",
            };

            cfg.AddInMemoryCollection(overrides);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptions<IdentityDbContext>>();
            services.RemoveAll<NpgsqlDataSource>();
            services.RemoveAll<IOtpChallengeDispatcher>();
            services.RemoveAll<IIdentityEmailDispatcher>();

            var connectionString = _postgres.GetConnectionString();
            services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseNpgsql(connectionString);
                options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
            });
            services.AddDbContext<IdentityDbContext>((provider, options) =>
            {
                options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>());
                options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
                options.AddInterceptors(provider.GetRequiredService<IdentitySaveChangesInterceptor>());
            });

            services.AddSingleton<IdentityDispatchCaptureStore>();
            services.AddScoped<IOtpChallengeDispatcher, TestOtpChallengeDispatcher>();
            services.AddScoped<IIdentityEmailDispatcher, TestIdentityEmailDispatcher>();
        });
    }

    private async Task EnsureMigrationsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await appDb.Database.MigrateAsync();

        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await identityDb.Database.MigrateAsync();
    }

    private static string CreatePrivateKeyPem()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return ecdsa.ExportECPrivateKeyPem();
    }
}
