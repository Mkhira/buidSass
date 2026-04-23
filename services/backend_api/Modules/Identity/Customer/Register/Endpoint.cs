using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.Routing;
using System.Diagnostics;

namespace BackendApi.Modules.Identity.Customer.Register;

public static class RegisterEndpoint
{
    public static IEndpointRouteBuilder MapRegisterEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/register", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        RegisterRequest request,
        HttpContext httpContext,
        IdentityDbContext dbContext,
        Argon2idHasher hasher,
        BreachListChecker breachListChecker,
        PhoneNormalizer phoneNormalizer,
        IdentityTokenSecretHasher tokenSecretHasher,
        IIdentityEmailDispatcher emailDispatcher,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var validator = new RegisterRequestValidator();
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
                "identity.register.invalid_request",
                "Invalid registration request",
                validation.Errors.First().ErrorMessage);
        }

        var result = await RegisterHandler.HandleAsync(
            request,
            dbContext,
            hasher,
            breachListChecker,
            phoneNormalizer,
            tokenSecretHasher,
            emailDispatcher,
            CustomerIdentityResponseFactory.ResolveCorrelationId(httpContext),
            ResolveActorIpHash(httpContext),
            auditEventPublisher,
            cancellationToken);

        await ConstantTimeOperation.EnsureMinimumDurationAsync(
            startedAt,
            TimeSpan.FromMilliseconds(500),
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

        return Results.Accepted(
            value: new RegisterAcceptedResponse("pending_email_verification"));
    }

    private static string? ResolveActorIpHash(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrWhiteSpace(remoteIp))
        {
            return null;
        }

        return Convert.ToHexString(CustomerIdentityResponseFactory.HashString(remoteIp));
    }
}
