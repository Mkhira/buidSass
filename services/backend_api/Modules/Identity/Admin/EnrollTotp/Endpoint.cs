using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Admin.EnrollTotp;

public static class EnrollTotpEndpoint
{
    public static IEndpointRouteBuilder MapEnrollTotpEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/mfa/totp/enroll", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        EnrollTotpRequest request,
        HttpContext httpContext,
        IdentityDbContext dbContext,
        AdminPartialAuthTokenStore partialAuthStore,
        Argon2idHasher hasher,
        IDataProtectionProvider dataProtectionProvider,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var validator = new EnrollTotpRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return AdminIdentityResponseFactory.Problem(
                httpContext,
                StatusCodes.Status400BadRequest,
                "identity.mfa.enroll.invalid_request",
                "Invalid TOTP enrollment request",
                validation.Errors.First().ErrorMessage);
        }

        var result = await EnrollTotpHandler.HandleAsync(
            request,
            dbContext,
            partialAuthStore,
            hasher,
            dataProtectionProvider,
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

        return Results.Ok(new EnrollTotpResponse(result.FactorId, result.OtpauthUri!, result.RecoveryCodes!));
    }
}
