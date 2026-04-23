using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace BackendApi.Modules.Identity.Admin.SignIn;

public static class AdminSignInEndpoint
{
    public static IEndpointRouteBuilder MapAdminSignInEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapPost("/sign-in", HandleAsync)
            .RequireRateLimiting(RateLimitPolicies.AdminSignIn);

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        AdminSignInRequest request,
        HttpContext httpContext,
        IdentityDbContext dbContext,
        Argon2idHasher hasher,
        AdminMfaChallengeStore mfaChallengeStore,
        IOptions<IdentityMfaOptions> mfaOptions,
        AdminAuthSessionService authSessionService,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var validator = new AdminSignInRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            await ConstantTimeOperation.EnsureMinimumDurationAsync(
                startedAt,
                TimeSpan.FromMilliseconds(500),
                cancellationToken);
            return AdminIdentityResponseFactory.Problem(
                httpContext,
                StatusCodes.Status400BadRequest,
                "identity.sign_in.invalid_request",
                "Invalid sign-in request",
                validation.Errors.First().ErrorMessage);
        }

        var result = await AdminSignInHandler.HandleAsync(
            request,
            httpContext,
            dbContext,
            hasher,
            mfaChallengeStore,
            mfaOptions,
            authSessionService,
            auditEventPublisher,
            cancellationToken);

        await ConstantTimeOperation.EnsureMinimumDurationAsync(
            startedAt,
            TimeSpan.FromMilliseconds(500),
            cancellationToken);
        if (!result.IsSuccess)
        {
            return AdminIdentityResponseFactory.Problem(
                httpContext,
                result.StatusCode,
                result.ReasonCode!,
                result.Title!,
                result.Detail!,
                result.Extensions);
        }

        if (result.IsMfaRequired)
        {
            return Results.Ok(new AdminSignInResponse(new AdminMfaChallengeEnvelope(result.ChallengeId!.Value, "totp"), null));
        }

        return Results.Ok(new AdminSignInResponse(null, result.AuthSession));
    }
}
