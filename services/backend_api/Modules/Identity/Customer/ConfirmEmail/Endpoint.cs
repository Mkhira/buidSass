using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Customer.ConfirmEmail;

public static class ConfirmEmailEndpoint
{
    public static IEndpointRouteBuilder MapConfirmEmailEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/email/confirm", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        ConfirmEmailRequest request,
        HttpContext httpContext,
        IdentityDbContext dbContext,
        IdentityTokenSecretHasher tokenSecretHasher,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var validator = new ConfirmEmailRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CustomerIdentityResponseFactory.Problem(
                httpContext,
                StatusCodes.Status400BadRequest,
                "identity.email_verification.invalid",
                "Invalid verification request",
                validation.Errors.First().ErrorMessage);
        }

        var result = await ConfirmEmailHandler.HandleAsync(
            request,
            dbContext,
            tokenSecretHasher,
            auditEventPublisher,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return CustomerIdentityResponseFactory.Problem(
                httpContext,
                result.StatusCode,
                result.ReasonCode!,
                result.Title!,
                result.Detail!);
        }

        return Results.Ok();
    }
}
