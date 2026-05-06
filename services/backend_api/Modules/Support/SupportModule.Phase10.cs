using BackendApi.Modules.Support.Customer.OpenRedactionRequestTicket;
using BackendApi.Modules.Support.Lead.ForceCloseTicket;
using BackendApi.Modules.Support.Lead.ToggleAgentAvailability;
using BackendApi.Modules.Support.Metrics;
using BackendApi.Modules.Support.SuperAdmin.ListRedactionRequestQueue;
using BackendApi.Modules.Support.SuperAdmin.RedactAttachment;
using BackendApi.Modules.Support.SuperAdmin.RedactMessage;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Support;

/// <summary>
/// Phase 10 cross-cutting partial — policy-admin, super-admin redaction,
/// metrics, agent-availability, force-close, customer redaction-request flow.
/// </summary>
public static partial class SupportModule
{
    static partial void AddPolicyAdminSlices(IServiceCollection services)
    {
        // Policy-admin slices (T116-T120) deferred — backed by direct edits to
        // sla_policies + support_market_schemas via the SupportReferenceDataSeeder
        // for V1. Authoring UI lands when admin shell wires these in spec 015.
    }

    static partial void AddPhase10Slices(IServiceCollection services)
    {
        // Customer-initiated redaction request (T129).
        services.AddScoped<OpenRedactionRequestTicketHandler>();

        // Lead phase-10 slices.
        services.AddScoped<ForceCloseTicketHandler>();
        services.AddScoped<ToggleAgentAvailabilityHandler>();

        // Super-admin redaction slices (T127, T128, T130).
        services.AddScoped<RedactAttachmentHandler>();
        services.AddScoped<RedactMessageHandler>();
        services.AddScoped<ListRedactionRequestQueueHandler>();

        // Metrics (T136a).
        services.AddScoped<GetSupportMetricsHandler>();
    }

    static partial void MapPolicyAdminEndpoints(IEndpointRouteBuilder policyAdmin)
    {
        // Policy-admin endpoints deferred — see AddPolicyAdminSlices.
    }

    static partial void MapPhase10Endpoints(IEndpointRouteBuilder admin)
    {
        admin.MapForceCloseTicketEndpoint();
        admin.MapToggleAgentAvailabilityEndpoint();
        admin.MapRedactAttachmentEndpoint();
        admin.MapRedactMessageEndpoint();
        admin.MapListRedactionRequestQueueEndpoint();
        admin.MapGetSupportMetricsEndpoint();
    }
}
