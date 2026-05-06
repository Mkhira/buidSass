using BackendApi.Modules.Support.Customer;
using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.Agent.DeleteForbidden;

/// <summary>
/// FR-005a — DELETE /api/admin/support-tickets/{id} returns 405 unconditionally.
/// </summary>
public static class DeleteForbiddenEndpoint
{
    public static IEndpointRouteBuilder MapDeleteForbiddenEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapDelete("/{ticketId:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static IResult HandleAsync(Guid ticketId, HttpContext context) =>
        SupportResponseFactory.Problem(context, 405,
            TicketReasonCode.RowDeleteForbidden,
            "Hard-deleting a support ticket is forbidden.",
            "Use the close transition for terminal state.");
}
