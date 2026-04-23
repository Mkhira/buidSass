using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Customer.CompletePasswordReset;

public static class CompletePasswordResetEndpoint
{
    public static IEndpointRouteBuilder MapCompletePasswordResetEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/password/reset-complete", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        CompletePasswordResetRequest request,
        HttpContext context,
        IdentityDbContext dbContext,
        Argon2idHasher hasher,
        BreachListChecker breachListChecker,
        IdentityTokenSecretHasher tokenSecretHasher,
        IRefreshTokenRevocationStore revocationStore,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var validator = new CompletePasswordResetRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CustomerIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "identity.password_reset.invalid_request",
                "Invalid password reset completion request",
                validation.Errors.First().ErrorMessage);
        }

        var result = await CompletePasswordResetHandler.HandleAsync(
            request,
            dbContext,
            hasher,
            breachListChecker,
            tokenSecretHasher,
            revocationStore,
            auditEventPublisher,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return CustomerIdentityResponseFactory.Problem(
                context,
                result.StatusCode,
                result.ReasonCode!,
                result.Title!,
                result.Detail!);
        }

        return Results.Ok();
    }
}
