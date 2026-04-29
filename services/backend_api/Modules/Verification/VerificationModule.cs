using BackendApi.Configuration;
using BackendApi.Features.Seeding;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Verification.Admin.DecideApprove;
using BackendApi.Modules.Verification.Admin.GetVerificationDetail;
using BackendApi.Modules.Verification.Admin.ListVerificationQueue;
using BackendApi.Modules.Verification.Customer.SubmitVerification;
using BackendApi.Modules.Verification.Eligibility;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Seeding;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace BackendApi.Modules.Verification;

public static class VerificationModule
{
    public static IServiceCollection AddVerificationModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var connectionString = configuration.ResolveRequiredDefaultConnectionString(hostEnvironment);

        // Scoped DbContext for the request pipeline.
        services.AddDbContext<VerificationDbContext>((provider, options) =>
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
            // The warning is suppressed inside VerificationDbContext.OnConfiguring as well;
            // belt-and-braces so factories created outside the AddDbContext path also
            // inherit it (project-memory rule).
        });

        // Factory for hosted workers (Expiry, Reminder, DocumentPurge — Phase 6) that
        // construct scopes outside the request pipeline (T031).
        services.AddDbContextFactory<VerificationDbContext>((provider, options) =>
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
        }, lifetime: ServiceLifetime.Singleton);

        // V1 default for the regulator-assist lookup is the null implementation
        // (FR-016a / FR-016b). Phase 1.5+ may swap a real adapter without
        // contract changes.
        services.AddSingleton<IRegulatorAssistLookup, NullRegulatorAssistLookup>();

        // Reference-data seeder: KSA + EG market schemas.
        services.AddScoped<ISeeder, VerificationReferenceDataSeeder>();

        // Phase 3 — customer slice handlers.
        services.AddScoped<EligibilityCacheInvalidator>();
        services.AddScoped<SubmitVerificationHandler>();

        // Phase 4 / US2 — reviewer slice handlers (Batch 1: queue + detail + approve).
        services.AddScoped<ListVerificationQueueHandler>();
        services.AddScoped<GetVerificationDetailHandler>();
        services.AddScoped<DecideApproveHandler>();

        // Time abstraction for testability.
        services.AddSingleton(TimeProvider.System);

        return services;
    }

    /// <summary>
    /// Mounts the verification HTTP surface. Phase 3 ships the customer
    /// submission endpoint under <c>/api/customer/verifications</c>; later
    /// phases attach the read endpoints + admin queue under their respective
    /// route groups.
    /// </summary>
    public static IEndpointRouteBuilder MapVerificationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var customer = endpoints.MapGroup("/api/customer/verifications");
        customer.MapSubmitVerificationEndpoint();

        var admin = endpoints.MapGroup("/api/admin/verifications");
        admin.MapListVerificationQueueEndpoint();
        admin.MapGetVerificationDetailEndpoint();
        admin.MapDecideApproveEndpoint();

        return endpoints;
    }
}
