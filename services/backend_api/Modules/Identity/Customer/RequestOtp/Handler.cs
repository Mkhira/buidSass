using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Customer.RequestOtp;

public static class RequestOtpHandler
{
    public static async Task<RequestOtpHandlerResult> HandleAsync(
        RequestOtpRequest request,
        HttpContext httpContext,
        IdentityDbContext dbContext,
        PhoneNormalizer phoneNormalizer,
        Argon2idHasher hasher,
        IdentityClientSecurityHasher clientSecurityHasher,
        IOtpChallengeDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        NormalizedPhone normalizedPhone;
        try
        {
            normalizedPhone = phoneNormalizer.Normalize(request.Phone);
        }
        catch (InvalidOperationException ex)
        {
            return RequestOtpHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.register.invalid_phone",
                "Invalid phone number",
                ex.Message);
        }

        var code = CustomerIdentityResponseFactory.CreateNumericOtpCode(6);
        var now = DateTimeOffset.UtcNow;

        var account = await dbContext.Accounts.SingleOrDefaultAsync(
            x => x.Surface == "customer" && x.PhoneE164 == normalizedPhone.E164,
            cancellationToken);
        if (account is null)
        {
            _ = hasher.HashPassword(request.Phone, SurfaceKind.Customer);
            _ = await dbContext.Accounts
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Select(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var destinationHash = clientSecurityHasher.HashIdentifier(normalizedPhone.E164);
        var normalizedPurpose = NormalizePurpose(request.Purpose);

        var recentCount = await dbContext.OtpChallenges
            .Where(x => x.Surface == "customer"
                        && x.Purpose == normalizedPurpose
                        && x.DestinationHash == destinationHash
                        && x.CreatedAt > now.AddMinutes(-10))
            .CountAsync(cancellationToken);

        if (recentCount >= 3)
        {
            return RequestOtpHandlerResult.Fail(
                StatusCodes.Status429TooManyRequests,
                "identity.rate_limit.otp",
                "Too many OTP requests",
                "Please try again later.");
        }

        var priorPending = await dbContext.OtpChallenges
            .Where(x => x.Surface == "customer"
                        && x.Purpose == normalizedPurpose
                        && x.DestinationHash == destinationHash
                        && x.Status == "pending")
            .ToListAsync(cancellationToken);

        foreach (var pending in priorPending)
        {
            pending.Status = "superseded";
            pending.CompletedAt = now;
        }

        var challenge = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            AccountId = account?.Id,
            Purpose = normalizedPurpose,
            Surface = "customer",
            Channel = "sms",
            DestinationHash = destinationHash,
            CodeHash = CustomerIdentityResponseFactory.HashString(code),
            CodeLength = 6,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(5),
            MaxAttempts = 3,
            Attempts = 0,
            Status = "pending",
        };

        dbContext.OtpChallenges.Add(challenge);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await dispatcher.DispatchAsync(
                new OtpChallengeDispatchRequest(
                    challenge.Id,
                    SurfaceKind.Customer,
                    normalizedPhone.E164,
                    challenge.Purpose,
                    code,
                    CustomerIdentityResponseFactory.ResolveCorrelationId(httpContext)),
                cancellationToken);
        }
        catch (IdentityDeliveryNotConfiguredException)
        {
            return RequestOtpHandlerResult.Fail(
                StatusCodes.Status503ServiceUnavailable,
                "identity.otp.dispatch_unavailable",
                "OTP service unavailable",
                "OTP delivery is temporarily unavailable.");
        }

        return RequestOtpHandlerResult.Accepted(challenge.Id);
    }

    private static string NormalizePurpose(string purpose)
    {
        var normalized = purpose.Trim().ToLowerInvariant();
        return normalized switch
        {
            "password_reset_confirm" => "password_reset_phone",
            _ => normalized,
        };
    }
}

public sealed record RequestOtpHandlerResult(
    bool IsSuccess,
    Guid ChallengeId,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail)
{
    public static RequestOtpHandlerResult Accepted(Guid challengeId)
    {
        return new RequestOtpHandlerResult(
            true,
            challengeId,
            StatusCodes.Status202Accepted,
            null,
            null,
            null);
    }

    public static RequestOtpHandlerResult Fail(
        int statusCode,
        string reasonCode,
        string title,
        string detail)
    {
        return new RequestOtpHandlerResult(
            false,
            Guid.Empty,
            statusCode,
            reasonCode,
            title,
            detail);
    }
}
