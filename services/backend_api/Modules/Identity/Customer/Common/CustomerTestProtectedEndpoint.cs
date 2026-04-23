using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Customer.Common;

public static class CustomerTestProtectedEndpoint
{
    public static IEndpointRouteBuilder MapCustomerTestProtectedEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapGet("/_test/protected", () => Results.Ok(new { ok = true }))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });

        return builder;
    }
}
