using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.Routing;
using System.Diagnostics;

namespace BackendApi.Modules.Identity.Customer.RequestPasswordReset;

public static class RequestPasswordResetEndpoint
{
    public static IEndpointRouteBuilder MapRequestPasswordResetEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapPost("/password/reset-request", HandleAsync)
            .RequireRateLimiting(RateLimitPolicies.PasswordResetRequest);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        RequestPasswordResetRequest request,
        HttpContext context,
        IdentityDbContext dbContext,
        Argon2idHasher hasher,
        IdentityTokenSecretHasher tokenSecretHasher,
        IIdentityEmailDispatcher emailDispatcher,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var validator = new RequestPasswordResetRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            await ConstantTimeOperation.EnsureMinimumDurationAsync(
                startedAt,
                TimeSpan.FromMilliseconds(500),
                cancellationToken);
            return CustomerIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "identity.password_reset.invalid_request",
                "Invalid password reset request",
                validation.Errors.First().ErrorMessage);
        }

        var result = await RequestPasswordResetHandler.HandleAsync(
            request,
            CustomerIdentityResponseFactory.ResolveCorrelationId(context),
            dbContext,
            hasher,
            tokenSecretHasher,
            emailDispatcher,
            cancellationToken);

        await ConstantTimeOperation.EnsureMinimumDurationAsync(
            startedAt,
            TimeSpan.FromMilliseconds(500),
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

        return Results.Accepted();
    }
}
