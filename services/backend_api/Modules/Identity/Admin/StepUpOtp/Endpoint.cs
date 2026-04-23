using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Admin.StepUpOtp;

public static class StepUpOtpEndpoint
{
    public static IEndpointRouteBuilder MapStepUpOtpEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapPost("/mfa/step-up", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequireRateLimiting(RateLimitPolicies.AdminOtpStepUp);

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        StepUpOtpRequest request,
        HttpContext context,
        IdentityDbContext dbContext,
        IOtpChallengeDispatcher dispatcher,
        IdentityClientSecurityHasher clientSecurityHasher,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var validator = new StepUpOtpRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return AdminIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "identity.step_up.invalid_request",
                "Invalid step-up request",
                validation.Errors.First().ErrorMessage);
        }

        var result = await StepUpOtpHandler.HandleAsync(
            context.User,
            context,
            request,
            dbContext,
            dispatcher,
            clientSecurityHasher,
            auditEventPublisher,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return AdminIdentityResponseFactory.Problem(
                context,
                result.StatusCode,
                result.ReasonCode!,
                result.Title!,
                result.Detail!);
        }

        return Results.Accepted(value: new StepUpOtpAcceptedResponse(result.ChallengeId));
    }
}
