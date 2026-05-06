using BackendApi.Modules.Support.Customer.OpenTicket;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Support;

/// <summary>
/// Customer surface partial — US1 slices.
/// </summary>
public static partial class SupportModule
{
    static partial void AddUs1Slices(IServiceCollection services)
    {
        services.AddScoped<OpenTicketHandler>();
    }

    static partial void MapUs1CustomerEndpoints(IEndpointRouteBuilder customer)
    {
        customer.MapOpenTicketEndpoint();
    }
}
