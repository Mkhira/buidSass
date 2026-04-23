using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Admin.AcceptInvitation;

public static class AcceptInvitationEndpoint
{
    public static IEndpointRouteBuilder MapAcceptInvitationEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/invitation/accept", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        AcceptInvitationRequest request,
        HttpContext httpContext,
        IdentityDbContext dbContext,
        Argon2idHasher hasher,
        BreachListChecker breachListChecker,
        AdminPartialAuthTokenStore partialAuthStore,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var validator = new AcceptInvitationRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return AdminIdentityResponseFactory.Problem(
                httpContext,
                StatusCodes.Status400BadRequest,
                "identity.invitation.invalid_request",
                "Invalid invitation request",
                validation.Errors.First().ErrorMessage);
        }

        var result = await AcceptInvitationHandler.HandleAsync(
            request,
            dbContext,
            hasher,
            breachListChecker,
            partialAuthStore,
            auditEventPublisher,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return AdminIdentityResponseFactory.Problem(
                httpContext,
                result.StatusCode,
                result.ReasonCode!,
                result.Title!,
                result.Detail!);
        }

        return Results.Accepted(value: new AcceptInvitationResponse(result.PartialAuthToken!));
    }
}
