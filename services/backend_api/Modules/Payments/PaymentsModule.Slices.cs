using BackendApi.Features.Seeding;
using BackendApi.Modules.Payments.Seeding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BackendApi.Modules.Payments;

/// <summary>
/// Slice + endpoint registrations for the Payments module. Each partial
/// hangs off <see cref="PaymentsModule"/> so adding a phase = appending one
/// method here, not touching <c>PaymentsModule.cs</c>.
/// </summary>
public static partial class PaymentsModule
{
    static partial void AddSlices(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        // MediatR — idempotent across modules; appends our assembly's handlers.
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<PaymentsModuleSlicesAnchor>());

        // Phase 2/3/4/5/6/7/8 slice handlers are wired here as they are added.
        AddPhase2Slices(services);
        AddPhase3Slices(services);
        AddPhase4Slices(services);
        AddPhase5Slices(services);
        AddPhase6Slices(services);
        AddPhase7Slices(services);
        AddPhase8Slices(services);

        // Phase 9 — reference-data seeder (idempotent in all environments).
        services.AddScoped<ISeeder, PaymentsV1Seeder>();
    }

    static partial void AddPhase2Slices(IServiceCollection services);
    static partial void AddPhase3Slices(IServiceCollection services);
    static partial void AddPhase4Slices(IServiceCollection services);
    static partial void AddPhase5Slices(IServiceCollection services);
    static partial void AddPhase6Slices(IServiceCollection services);
    static partial void AddPhase7Slices(IServiceCollection services);
    static partial void AddPhase8Slices(IServiceCollection services);

    static partial void MapCustomerEndpoints(IEndpointRouteBuilder customer)
    {
        MapCustomerEndpointsPhase2(customer);
        MapCustomerEndpointsPhase4(customer);
        MapCustomerEndpointsPhase5(customer);
    }

    static partial void MapCustomerEndpointsPhase2(IEndpointRouteBuilder customer);
    static partial void MapCustomerEndpointsPhase4(IEndpointRouteBuilder customer);
    static partial void MapCustomerEndpointsPhase5(IEndpointRouteBuilder customer);

    static partial void MapAdminEndpoints(IEndpointRouteBuilder admin)
    {
        MapAdminEndpointsPhase4(admin);
        MapAdminEndpointsPhase5(admin);
        MapAdminEndpointsPhase6(admin);
        MapAdminEndpointsPhase7(admin);
        MapAdminEndpointsPhase8(admin);
    }

    static partial void MapAdminEndpointsPhase4(IEndpointRouteBuilder admin);
    static partial void MapAdminEndpointsPhase5(IEndpointRouteBuilder admin);
    static partial void MapAdminEndpointsPhase6(IEndpointRouteBuilder admin);
    static partial void MapAdminEndpointsPhase7(IEndpointRouteBuilder admin);
    static partial void MapAdminEndpointsPhase8(IEndpointRouteBuilder admin);

    static partial void MapWebhookEndpoints(IEndpointRouteBuilder webhooks)
    {
        MapWebhookEndpointsPhase6(webhooks);
    }

    static partial void MapWebhookEndpointsPhase6(IEndpointRouteBuilder webhooks);
}

/// <summary>Type-anchor used to scope MediatR registration to this assembly partition.</summary>
internal sealed class PaymentsModuleSlicesAnchor { }
