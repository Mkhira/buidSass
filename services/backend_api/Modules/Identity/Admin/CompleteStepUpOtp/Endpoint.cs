using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Admin.CompleteStepUpOtp;

public static class CompleteStepUpOtpEndpoint
{
    public static IEndpointRouteBuilder MapCompleteStepUpOtpEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapPost("/mfa/step-up/confirm", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        CompleteStepUpOtpRequest request,
        HttpContext context,
        IdentityDbContext dbContext,
        IJwtIssuer jwtIssuer,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var validator = new CompleteStepUpOtpRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return AdminIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "identity.step_up.invalid_request",
                "Invalid step-up confirmation request",
                validation.Errors.First().ErrorMessage);
        }

        var result = await CompleteStepUpOtpHandler.HandleAsync(
            context.User,
            request,
            dbContext,
            jwtIssuer,
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

        return Results.Ok(new CompleteStepUpOtpResponse(
            result.AccessToken!,
            result.AccessTokenExpiresAt!.Value,
            result.StepUpValidUntil!.Value));
    }
}
