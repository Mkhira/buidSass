using BackendApi.Modules.Support.Authorization;
using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.Agent.RetagCategory;

public static class RetagCategoryEndpoint
{
    public sealed record RetagCategoryRequest(string Category, string? Justification);

    public static IEndpointRouteBuilder MapRetagCategoryEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{ticketId:guid}/retag-category", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid ticketId,
        [FromBody] RetagCategoryRequest? body,
        HttpContext context,
        [FromServices] RetagCategoryHandler handler,
        CancellationToken ct)
    {
        // CodeRabbit Loop-2: authn (401) before authz (403) so an
        // unauthenticated caller sees the correct status.
        var actorId = AdminSupportResponseFactory.ResolveActorId(context);
        if (actorId is null)
        {
            return AdminSupportResponseFactory.Problem(context, 401,
                TicketReasonCode.QueueForbidden, "Authentication required.");
        }
        if (!AdminSupportResponseFactory.HasAgentLevelAccess(context))
        {
            return AdminSupportResponseFactory.Problem(context, 403,
                TicketReasonCode.QueueForbidden,
                "support.agent permission required.");
        }
        if (body is null || string.IsNullOrWhiteSpace(body.Category))
        {
            return AdminSupportResponseFactory.Problem(context, 400,
                TicketReasonCode.InvalidTransition, "category is required.");
        }

        var isSuperAdmin = AdminSupportResponseFactory.HasSuperAdmin(context);
        var actorRole = isSuperAdmin
            ? SupportPermissions.SuperAdmin
            : (AdminSupportResponseFactory.HasLeadPermission(context)
                ? SupportPermissions.SupportLead
                : SupportPermissions.SupportAgent);
        var marketCode = AdminSupportResponseFactory.ResolveMarketCode(context);

        var result = await handler.HandleAsync(new RetagCategoryCommand(
            TicketId: ticketId,
            ActorId: actorId.Value,
            ActorRole: actorRole,
            NewCategory: body.Category,
            Justification: body.Justification,
            MarketCode: marketCode,
            IsSuperAdmin: isSuperAdmin), ct);

        if (!result.Success)
        {
            // CodeRabbit Loop-3: map authorization-style failures to 403 so
            // a foreign-market read (which surfaces as `LinkedEntityNotFound`
            // by design) and a permission failure (`QueueForbidden`) both
            // produce semantically-correct HTTP statuses.
            var status = result.ReasonCode switch
            {
                TicketReasonCode.LinkedEntityNotFound => 404,
                TicketReasonCode.ClosedTerminal => 409,
                TicketReasonCode.VersionConflict => 409,
                TicketReasonCode.QueueForbidden => 403,
                _ => 400,
            };
            return AdminSupportResponseFactory.Problem(context, status,
                result.ReasonCode ?? TicketReasonCode.InvalidTransition,
                result.Detail ?? "Retag failed.");
        }

        return Results.Ok(new
        {
            ticket_id = ticketId,
            prior_category = result.PriorCategory,
            new_category = result.NewCategory,
        });
    }
}
