using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Customer.VerifyOtp;

public static class VerifyOtpEndpoint
{
    public static IEndpointRouteBuilder MapVerifyOtpEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/otp/verify", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        VerifyOtpRequest request,
        HttpContext httpContext,
        IdentityDbContext dbContext,
        CustomerAuthSessionService authSessionService,
        IdentityClientSecurityHasher clientSecurityHasher,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var validator = new VerifyOtpRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CustomerIdentityResponseFactory.Problem(
                httpContext,
                StatusCodes.Status400BadRequest,
                "identity.otp.invalid_request",
                "Invalid OTP verification request",
                validation.Errors.First().ErrorMessage);
        }

        var result = await VerifyOtpHandler.HandleAsync(
            request,
            httpContext,
            dbContext,
            authSessionService,
            clientSecurityHasher,
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

        return result.Session is null ? Results.Ok() : Results.Ok(result.Session);
    }
}
