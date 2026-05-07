using BackendApi.Modules.Support.Lead.OverrideSlaTargets;
using BackendApi.Modules.Support.Lead.ReassignTicket;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Support;

/// <summary>Lead surface partial — US4 / Phase 10 lead slices.</summary>
public static partial class SupportModule
{
    static partial void AddUs4Slices(IServiceCollection services)
    {
        services.AddScoped<ReassignTicketHandler>();
        services.AddScoped<OverrideSlaTargetsHandler>();
    }

    static partial void MapUs4LeadEndpoints(IEndpointRouteBuilder admin)
    {
        admin.MapReassignTicketEndpoint();
        admin.MapOverrideSlaTargetsEndpoint();
    }
}
