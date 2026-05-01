using BackendApi.Configuration;
using BackendApi.Features.Seeding;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using BackendApi.Modules.Cms.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace BackendApi.Modules.Cms;

/// <summary>
/// DI + endpoint wiring for the CMS vertical-slice module per spec 024.
/// Phase 2 ships persistence, primitives, and reference-data seeding;
/// per-user-story slice handlers, workers, subscribers, and storefront
/// engine land in subsequent phases per
/// <c>specs/phase-1D/024-cms/tasks.md</c>.
/// </summary>
public static class CmsModule
{
    public static IServiceCollection AddCmsModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var connectionString = configuration.ResolveRequiredDefaultConnectionString(hostEnvironment);

        services.AddDbContext<CmsDbContext>((provider, options) =>
        {
            var dataSource = provider.GetService<NpgsqlDataSource>();
            if (dataSource is not null)
            {
                options.UseNpgsql(dataSource);
            }
            else
            {
                options.UseNpgsql(connectionString);
            }
            // ManyServiceProvidersCreatedWarning suppressed (project-memory rule).
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        });

        services.AddDbContextFactory<CmsDbContext>((provider, options) =>
        {
            var dataSource = provider.GetService<NpgsqlDataSource>();
            if (dataSource is not null)
            {
                options.UseNpgsql(dataSource);
            }
            else
            {
                options.UseNpgsql(connectionString);
            }
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        }, lifetime: ServiceLifetime.Singleton);

        // Primitives — pure helpers reused across slices.
        services.AddSingleton<BannerCapacityCalculator>();
        services.AddSingleton<Storefront.StorefrontContentResolver>();

        // Reference-data seeding (idempotent across Dev / Staging / Production).
        services.AddScoped<ISeeder, CmsReferenceDataSeeder>();

        services.AddSingleton(TimeProvider.System);
        return services;
    }

    /// <summary>
    /// Overload preserved for the existing <c>Program.cs</c> wiring; resolves
    /// the host environment from configuration so the connection-string lookup
    /// still works.
    /// </summary>
    public static IServiceCollection AddCmsModule(
        this IServiceCollection services,
        IConfiguration configuration)
        => services.AddCmsModule(configuration, new ImplicitHostEnvironment(configuration));

    private sealed class ImplicitHostEnvironment(IConfiguration configuration) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } =
            configuration["DOTNET_ENVIRONMENT"]
            ?? configuration["ASPNETCORE_ENVIRONMENT"]
            ?? Environments.Production;
        public string ApplicationName { get; set; } = "backend_api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
