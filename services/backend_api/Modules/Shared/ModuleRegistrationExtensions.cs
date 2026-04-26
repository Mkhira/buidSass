using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Observability;
using BackendApi.Modules.Pdf;
using BackendApi.Modules.Observability.HealthChecks;
using BackendApi.Modules.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Shared;

public static class ModuleRegistrationExtensions
{
    public static IServiceCollection AddAuditLogModule(this IServiceCollection services)
    {
        services.AddScoped<IAuditEventPublisher, AuditEventPublisher>();
        return services;
    }

    public static IServiceCollection AddStorageModule(this IServiceCollection services)
    {
        services.AddScoped<IVirusScanService, LocalVirusScanService>();
        services.AddScoped<IStorageService, LocalDiskStorageService>();
        return services;
    }

    public static IServiceCollection AddPdfModule(this IServiceCollection services)
    {
        services.AddSingleton<PdfTemplateRegistry>();

        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (string.Equals(environment, Environments.Production, StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IPdfService, QuestPdfService>();
        }
        else
        {
            services.AddScoped<IPdfService, StubPdfService>();
        }

        return services;
    }

    public static IServiceCollection AddObservabilityModule(this IServiceCollection services)
    {
        services.AddSingleton<SearchMetrics>();
        services.AddSingleton<InventoryMetrics>();
        services.AddTransient<CorrelationIdDelegatingHandler>();
        services.AddHttpClient("default").AddHttpMessageHandler<CorrelationIdDelegatingHandler>();
        // Host-scoped singleton — gate is per DI container, not per process. Keeps test
        // isolation clean when multiple WebApplicationFactory instances live in one process.
        services.AddSingleton<DbConnectivityProbeGate>();
        services.AddHealthChecks()
            .AddCheck<DbConnectivityCheck>("db-connectivity")
            .AddCheck<StorageReachabilityCheck>("storage-reachability");
        return services;
    }
}
