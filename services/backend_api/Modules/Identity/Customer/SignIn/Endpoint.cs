using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.Routing;
using System.Diagnostics;

namespace BackendApi.Modules.Identity.Customer.SignIn;

public static class CustomerSignInEndpoint
{
    public static IEndpointRouteBuilder MapCustomerSignInEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapPost("/sign-in", HandleAsync)
            .RequireRateLimiting(RateLimitPolicies.CustomerSignIn);

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        CustomerSignInRequest request,
        HttpContext httpContext,
        IdentityDbContext dbContext,
        Argon2idHasher hasher,
        CustomerAuthSessionService authSessionService,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var validator = new CustomerSignInRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            await ConstantTimeOperation.EnsureMinimumDurationAsync(
                startedAt,
                TimeSpan.FromMilliseconds(500),
                cancellationToken);
            return CustomerIdentityResponseFactory.Problem(
                httpContext,
                StatusCodes.Status400BadRequest,
                "identity.sign_in.invalid_request",
                "Invalid sign-in request",
                validation.Errors.First().ErrorMessage);
        }

        var result = await CustomerSignInHandler.HandleAsync(
            request,
            httpContext,
            dbContext,
            hasher,
            authSessionService,
            auditEventPublisher,
            cancellationToken);

        await ConstantTimeOperation.EnsureMinimumDurationAsync(
            startedAt,
            TimeSpan.FromMilliseconds(500),
            cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.LockedUntil is DateTimeOffset lockedUntil)
            {
                return CustomerIdentityResponseFactory.Problem(
                    httpContext,
                    result.StatusCode,
                    result.ReasonCode!,
                    result.Title!,
                    result.Detail!,
                    new Dictionary<string, object?> { ["lockedUntil"] = lockedUntil });
            }

            return CustomerIdentityResponseFactory.Problem(
                httpContext,
                result.StatusCode,
                result.ReasonCode!,
                result.Title!,
                result.Detail!);
        }

        return Results.Ok(result.Session);
    }
}
