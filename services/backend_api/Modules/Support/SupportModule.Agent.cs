using BackendApi.Modules.Support.Agent.AddInternalNote;
using BackendApi.Modules.Support.Agent.ClaimTicket;
using BackendApi.Modules.Support.Agent.DeleteForbidden;
using BackendApi.Modules.Support.Agent.GetTicketAdminDetail;
using BackendApi.Modules.Support.Agent.ListAgentQueue;
using BackendApi.Modules.Support.Agent.ListTicketsByCustomer;
using BackendApi.Modules.Support.Agent.ReplyAsAgent;
using BackendApi.Modules.Support.Agent.RetagCategory;
using BackendApi.Modules.Support.Agent.TransitionToResolved;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Support;

/// <summary>Agent surface partial — US2 / US5 slices + FR-005a delete-forbidden.</summary>
public static partial class SupportModule
{
    static partial void AddUs2Slices(IServiceCollection services)
    {
        services.AddScoped<ListAgentQueueHandler>();
        services.AddScoped<ClaimTicketHandler>();
        services.AddScoped<ReplyAsAgentHandler>();
        services.AddScoped<AddInternalNoteHandler>();
        services.AddScoped<TransitionToResolvedHandler>();
        services.AddScoped<GetTicketAdminDetailHandler>();
        services.AddScoped<RetagCategoryHandler>();
        services.AddScoped<ListTicketsByCustomerHandler>();
    }

    static partial void MapUs2AgentEndpoints(IEndpointRouteBuilder admin)
    {
        admin.MapListAgentQueueEndpoint();
        admin.MapClaimTicketEndpoint();
        admin.MapReplyAsAgentEndpoint();
        admin.MapAddInternalNoteEndpoint();
        admin.MapTransitionToResolvedEndpoint();
        admin.MapDeleteForbiddenEndpoint();
        admin.MapGetTicketAdminDetailEndpoint();
        admin.MapRetagCategoryEndpoint();
        admin.MapListTicketsByCustomerEndpoint();
    }
}
