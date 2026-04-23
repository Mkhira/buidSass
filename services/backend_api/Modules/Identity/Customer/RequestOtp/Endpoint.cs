using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.Routing;
using System.Diagnostics;

namespace BackendApi.Modules.Identity.Customer.RequestOtp;

public static class RequestOtpEndpoint
{
    public static IEndpointRouteBuilder MapRequestOtpEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapPost("/otp/request", HandleAsync)
            .RequireRateLimiting(RateLimitPolicies.CustomerOtpRequest);

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        RequestOtpRequest request,
        HttpContext httpContext,
        IdentityDbContext dbContext,
        PhoneNormalizer phoneNormalizer,
        Argon2idHasher hasher,
        IdentityClientSecurityHasher clientSecurityHasher,
        IOtpChallengeDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var validator = new RequestOtpRequestValidator();
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
                "identity.otp.invalid_request",
                "Invalid OTP request",
                validation.Errors.First().ErrorMessage);
        }

        var result = await RequestOtpHandler.HandleAsync(
            request,
            httpContext,
            dbContext,
            phoneNormalizer,
            hasher,
            clientSecurityHasher,
            dispatcher,
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

        return Results.Accepted(value: new RequestOtpAcceptedResponse(result.ChallengeId));
    }
}
