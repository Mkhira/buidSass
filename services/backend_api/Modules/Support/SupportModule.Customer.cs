using BackendApi.Modules.Support.Customer.ConvertToReturnRequest;
using BackendApi.Modules.Support.Customer.GetMyTicket;
using BackendApi.Modules.Support.Customer.ListMyTickets;
using BackendApi.Modules.Support.Customer.OpenTicket;
using BackendApi.Modules.Support.Customer.ReplyAsCustomer;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Support;

/// <summary>
/// Customer surface partial — US1 + US3 slices.
/// </summary>
public static partial class SupportModule
{
    static partial void AddUs1Slices(IServiceCollection services)
    {
        services.AddScoped<OpenTicketHandler>();
        services.AddScoped<ReplyAsCustomerHandler>();
        services.AddScoped<ListMyTicketsHandler>();
        services.AddScoped<GetMyTicketHandler>();
        services.AddScoped<ConvertToReturnRequestHandler>();
    }

    static partial void MapUs1CustomerEndpoints(IEndpointRouteBuilder customer)
    {
        customer.MapOpenTicketEndpoint();
        customer.MapListMyTicketsEndpoint();
        customer.MapGetMyTicketEndpoint();
        customer.MapReplyAsCustomerEndpoint();
        customer.MapConvertToReturnRequestEndpoint();
    }
}
