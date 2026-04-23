using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Admin.CompleteMfaChallenge;

public static class CompleteMfaChallengeEndpoint
{
    public static IEndpointRouteBuilder MapCompleteMfaChallengeEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/mfa/challenge", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        CompleteMfaChallengeRequest request,
        HttpContext httpContext,
        IdentityDbContext dbContext,
        AdminMfaChallengeStore mfaChallengeStore,
        IDataProtectionProvider dataProtectionProvider,
        Argon2idHasher hasher,
        AdminAuthSessionService authSessionService,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var validator = new CompleteMfaChallengeRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return AdminIdentityResponseFactory.Problem(
                httpContext,
                StatusCodes.Status400BadRequest,
                "identity.mfa.challenge.invalid_request",
                "Invalid MFA challenge request",
                validation.Errors.First().ErrorMessage);
        }

        var result = await CompleteMfaChallengeHandler.HandleAsync(
            request,
            httpContext,
            dbContext,
            mfaChallengeStore,
            dataProtectionProvider,
            hasher,
            authSessionService,
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

        return Results.Ok(result.Session);
    }
}
