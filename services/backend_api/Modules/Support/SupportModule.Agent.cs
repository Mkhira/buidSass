using BackendApi.Modules.Support.Agent.ClaimTicket;
using BackendApi.Modules.Support.Agent.ListAgentQueue;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Support;

/// <summary>Agent surface partial — US2 slices.</summary>
public static partial class SupportModule
{
    static partial void AddUs2Slices(IServiceCollection services)
    {
        services.AddScoped<ListAgentQueueHandler>();
        services.AddScoped<ClaimTicketHandler>();
    }

    static partial void MapUs2AgentEndpoints(IEndpointRouteBuilder admin)
    {
        admin.MapListAgentQueueEndpoint();
        admin.MapClaimTicketEndpoint();
    }
}
