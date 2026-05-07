using BackendApi.Modules.Support.Customer.ConvertToReturnRequest;
using BackendApi.Modules.Support.Customer.GetMyTicket;
using BackendApi.Modules.Support.Customer.ListMyTickets;
using BackendApi.Modules.Support.Customer.OpenRedactionRequestTicket;
using BackendApi.Modules.Support.Customer.OpenTicket;
using BackendApi.Modules.Support.Customer.ReopenTicket;
using BackendApi.Modules.Support.Customer.ReplyAsCustomer;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Support;

/// <summary>Customer surface partial — US1 / US3 / US6 + Phase 10 customer slices.</summary>
public static partial class SupportModule
{
    static partial void AddUs1Slices(IServiceCollection services)
    {
        services.AddScoped<OpenTicketHandler>();
        services.AddScoped<ReplyAsCustomerHandler>();
        services.AddScoped<ListMyTicketsHandler>();
        services.AddScoped<GetMyTicketHandler>();
        services.AddScoped<ConvertToReturnRequestHandler>();
        services.AddScoped<ReopenTicketHandler>();
    }

    static partial void MapUs1CustomerEndpoints(IEndpointRouteBuilder customer)
    {
        customer.MapOpenTicketEndpoint();
        customer.MapListMyTicketsEndpoint();
        customer.MapGetMyTicketEndpoint();
        customer.MapReplyAsCustomerEndpoint();
        customer.MapConvertToReturnRequestEndpoint();
        customer.MapReopenTicketEndpoint();
        // Phase 10 T129 — customer-initiated redaction-request flow (FR-011a).
        customer.MapOpenRedactionRequestTicketEndpoint();
    }
}
