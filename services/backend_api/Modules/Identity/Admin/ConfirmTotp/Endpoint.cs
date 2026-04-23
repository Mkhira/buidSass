using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Admin.ConfirmTotp;

public static class ConfirmTotpEndpoint
{
    public static IEndpointRouteBuilder MapConfirmTotpEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/mfa/totp/confirm", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        ConfirmTotpRequest request,
        HttpContext httpContext,
        IdentityDbContext dbContext,
        AdminPartialAuthTokenStore partialAuthStore,
        IDataProtectionProvider dataProtectionProvider,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var validator = new ConfirmTotpRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return AdminIdentityResponseFactory.Problem(
                httpContext,
                StatusCodes.Status400BadRequest,
                "identity.mfa.confirm.invalid_request",
                "Invalid TOTP confirmation request",
                validation.Errors.First().ErrorMessage);
        }

        var result = await ConfirmTotpHandler.HandleAsync(
            request,
            dbContext,
            partialAuthStore,
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

        return Results.Ok();
    }
}
