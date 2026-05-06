using BackendApi.Configuration;
using BackendApi.Features.Seeding;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using BackendApi.Modules.Support.Seeding;
using BackendApi.Modules.Support.Subscribers;
using MediatR;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace BackendApi.Modules.Support;

/// <summary>
/// DI + endpoint wiring for the Support vertical-slice module per spec 023.
/// Slice handlers are registered as they are introduced; the partial files
/// (<c>SupportModule.Customer.cs</c>, <c>SupportModule.Agent.cs</c>, etc.)
/// own their own slice registrations.
/// </summary>
public static partial class SupportModule
{
    public static IServiceCollection AddSupportModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var connectionString = configuration.ResolveRequiredDefaultConnectionString(hostEnvironment);

        services.AddDbContext<SupportDbContext>((provider, options) =>
        {
            var dataSource = provider.GetService<NpgsqlDataSource>();
            if (dataSource is not null) options.UseNpgsql(dataSource);
            else options.UseNpgsql(connectionString);
            // ManyServiceProvidersCreatedWarning suppressed (project-memory rule).
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        });

        services.AddDbContextFactory<SupportDbContext>((provider, options) =>
        {
            var dataSource = provider.GetService<NpgsqlDataSource>();
            if (dataSource is not null) options.UseNpgsql(dataSource);
            else options.UseNpgsql(connectionString);
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        }, lifetime: ServiceLifetime.Singleton);

        services.AddScoped<ISeeder, SupportReferenceDataSeeder>();

        // MediatR — idempotent across modules; appends our assembly's handlers.
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<TicketOpened>());

        // Cross-module fallback bindings (TryAdd pattern) — replaced by their
        // owning specs at runtime when their modules are loaded. Same loose-coupling
        // pattern as Reviews / Verification.
        services.TryAddScoped<IOrderLinkedReadContract, NullOrderLinkedReadContract>();
        services.TryAddScoped<IReturnLinkedReadContract, NullReturnLinkedReadContract>();
        services.TryAddScoped<IQuoteLinkedReadContract, NullQuoteLinkedReadContract>();
        services.TryAddScoped<IReviewLinkedReadContract, NullReviewLinkedReadContract>();
        services.TryAddScoped<IVerificationLinkedReadContract, NullVerificationLinkedReadContract>();
        services.TryAddScoped<ICompanyAccountQuery, NullCompanyAccountQuery>();
        services.TryAddScoped<IReturnRequestCreationContract, NullReturnRequestCreationContract>();

        services.AddScoped<MarketCodeResolver>();

        // Subscriber bindings (cross-module event consumers).
        services.AddScoped<IReturnOutcomeSubscriber, ReturnOutcomeHandler>();

        // Per-phase slice registrations.
        AddUs1Slices(services);
        AddUs2Slices(services);
        AddUs4Slices(services);
        AddPolicyAdminSlices(services);
        AddPhase10Slices(services);
        AddSupportWorkers(services, configuration);
        AddSupportLifecycleSubscribers(services);

        services.TryAddSingleton(TimeProvider.System);
        return services;
    }

    public static IEndpointRouteBuilder MapSupportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var customer = endpoints.MapGroup("/api/customer/support-tickets");
        MapUs1CustomerEndpoints(customer);

        var admin = endpoints.MapGroup("/api/admin/support-tickets");
        MapUs2AgentEndpoints(admin);
        MapUs4LeadEndpoints(admin);
        MapPhase10Endpoints(admin);

        var policyAdmin = endpoints.MapGroup("/api/admin/support-policies");
        MapPolicyAdminEndpoints(policyAdmin);
        return endpoints;
    }

    static partial void AddUs1Slices(IServiceCollection services);
    static partial void AddUs2Slices(IServiceCollection services);
    static partial void AddUs4Slices(IServiceCollection services);
    static partial void AddPolicyAdminSlices(IServiceCollection services);
    static partial void AddPhase10Slices(IServiceCollection services);
    static partial void AddSupportWorkers(IServiceCollection services, IConfiguration configuration);
    static partial void AddSupportLifecycleSubscribers(IServiceCollection services);
    static partial void MapUs1CustomerEndpoints(IEndpointRouteBuilder customer);
    static partial void MapUs2AgentEndpoints(IEndpointRouteBuilder admin);
    static partial void MapUs4LeadEndpoints(IEndpointRouteBuilder admin);
    static partial void MapPhase10Endpoints(IEndpointRouteBuilder admin);
    static partial void MapPolicyAdminEndpoints(IEndpointRouteBuilder policyAdmin);
}
