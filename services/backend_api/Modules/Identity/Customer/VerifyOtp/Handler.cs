using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using BackendApi.Modules.Identity.Primitives.StateMachines;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace BackendApi.Modules.Identity.Customer.VerifyOtp;

public static class VerifyOtpHandler
{
    public static async Task<VerifyOtpHandlerResult> HandleAsync(
        VerifyOtpRequest request,
        HttpContext httpContext,
        IdentityDbContext dbContext,
        CustomerAuthSessionService authSessionService,
        IdentityClientSecurityHasher clientSecurityHasher,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var challenge = await dbContext.OtpChallenges.SingleOrDefaultAsync(x => x.Id == request.ChallengeId, cancellationToken);
        if (challenge is null)
        {
            return VerifyOtpHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.otp.invalid",
                "Invalid OTP",
                "The OTP challenge could not be found.");
        }

        var stateMachine = new OtpChallengeStateMachine();
        var now = DateTimeOffset.UtcNow;

        if (!string.Equals(challenge.Status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            return BuildStatusFailure(challenge.Status);
        }

        var normalizedIdentifier = NormalizeIdentifier(request.Identifier);
        var destinationHash = clientSecurityHasher.HashIdentifier(normalizedIdentifier);
        if (!CustomerIdentityResponseFactory.FixedTimeEquals(destinationHash, challenge.DestinationHash))
        {
            return VerifyOtpHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.otp.invalid",
                "Invalid OTP",
                "The submitted OTP code is invalid.");
        }

        var actorAccountId = ResolveSubjectAccountId(httpContext.User);
        if (actorAccountId is Guid subjectAccountId
            && challenge.AccountId is Guid challengeAccountId
            && subjectAccountId != challengeAccountId)
        {
            return VerifyOtpHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.otp.invalid",
                "Invalid OTP",
                "The submitted OTP code is invalid.");
        }

        if (challenge.ExpiresAt <= now)
        {
            _ = stateMachine.TryTransition(OtpChallengeState.Pending, OtpChallengeTrigger.Expires, out _);
            challenge.Status = "expired";
            challenge.CompletedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);

            return VerifyOtpHandlerResult.Fail(
                StatusCodes.Status410Gone,
                "identity.otp.expired",
                "OTP expired",
                "The OTP has expired.");
        }

        var submittedCodeHash = CustomerIdentityResponseFactory.HashString(request.Code);
        if (!CustomerIdentityResponseFactory.FixedTimeEquals(submittedCodeHash, challenge.CodeHash))
        {
            challenge.Attempts += 1;
            if (challenge.Attempts >= challenge.MaxAttempts)
            {
                _ = stateMachine.TryTransition(OtpChallengeState.Pending, OtpChallengeTrigger.MaxAttemptsReached, out _);
                challenge.Status = "exhausted";
                challenge.CompletedAt = now;
                await dbContext.SaveChangesAsync(cancellationToken);

                return VerifyOtpHandlerResult.Fail(
                    StatusCodes.Status429TooManyRequests,
                    "identity.otp.exhausted",
                    "OTP attempts exhausted",
                    "The OTP challenge has reached its maximum number of attempts.");
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return VerifyOtpHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.otp.invalid",
                "Invalid OTP",
                "The submitted OTP code is invalid.");
        }

        _ = stateMachine.TryTransition(OtpChallengeState.Pending, OtpChallengeTrigger.VerifySuccess, out _);
        challenge.Status = "completed";
        challenge.CompletedAt = now;
        var normalizedPurpose = NormalizePurpose(challenge.Purpose);

        Account? account = null;
        AuthSessionResponse? session = null;
        string? auditAction = null;
        string? auditReason = null;

        switch (normalizedPurpose)
        {
            case "registration_phone":
                if (challenge.AccountId is Guid registrationAccountId)
                {
                    account = await dbContext.Accounts.SingleOrDefaultAsync(x => x.Id == registrationAccountId, cancellationToken);
                    if (account is not null)
                    {
                        account.PhoneVerifiedAt = now;
                        if (string.Equals(account.Status, "pending_phone_verification", StringComparison.OrdinalIgnoreCase)
                            && account.EmailVerifiedAt is not null)
                        {
                            account.Status = "active";
                        }

                        account.UpdatedAt = now;
                    }
                }

                auditAction = "phone.verified";
                auditReason = "customer.otp.verify";
                break;
            case "signin_customer":
                if (challenge.AccountId is not Guid signInAccountId)
                {
                    return VerifyOtpHandlerResult.Fail(
                        StatusCodes.Status400BadRequest,
                        "identity.otp.invalid",
                        "Invalid OTP",
                        "The submitted OTP code is invalid.");
                }

                account = await dbContext.Accounts.SingleOrDefaultAsync(x => x.Id == signInAccountId, cancellationToken);
                if (account is null)
                {
                    return VerifyOtpHandlerResult.Fail(
                        StatusCodes.Status400BadRequest,
                        "identity.otp.invalid",
                        "Invalid OTP",
                        "The submitted OTP code is invalid.");
                }

                session = authSessionService.CreateForNewSession(account, httpContext);
                auditAction = "customer.signin.succeeded";
                auditReason = "customer.otp.signin";
                break;
            case "password_reset_phone":
                if (challenge.AccountId is Guid passwordResetAccountId)
                {
                    account = await dbContext.Accounts.SingleOrDefaultAsync(x => x.Id == passwordResetAccountId, cancellationToken);
                }

                auditAction = "password_reset.phone_verified";
                auditReason = "customer.password_reset.otp";
                break;
            case "step_up_customer":
                if (challenge.AccountId is Guid stepUpAccountId)
                {
                    account = await dbContext.Accounts.SingleOrDefaultAsync(x => x.Id == stepUpAccountId, cancellationToken);
                }

                auditAction = "step_up.passed";
                auditReason = "customer.step_up.otp";
                break;
            default:
                return VerifyOtpHandlerResult.Fail(
                    StatusCodes.Status400BadRequest,
                    "identity.otp.invalid_request",
                    "Unsupported OTP purpose",
                    "The OTP purpose is unsupported.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (account is not null && auditAction is not null && auditReason is not null)
        {
            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: account.Id,
                    ActorRole: "customer",
                    Action: auditAction,
                    EntityType: nameof(Account),
                    EntityId: account.Id,
                    BeforeState: null,
                    AfterState: new
                    {
                        AccountId = account.Id,
                        challenge.Purpose,
                        account.PhoneVerifiedAt,
                        ChallengeId = challenge.Id,
                    },
                    Reason: auditReason),
                cancellationToken);
        }

        return VerifyOtpHandlerResult.Success(session);
    }

    private static VerifyOtpHandlerResult BuildStatusFailure(string status)
    {
        return status.Trim().ToLowerInvariant() switch
        {
            "expired" => VerifyOtpHandlerResult.Fail(
                StatusCodes.Status410Gone,
                "identity.otp.expired",
                "OTP expired",
                "The OTP has expired."),
            "exhausted" => VerifyOtpHandlerResult.Fail(
                StatusCodes.Status429TooManyRequests,
                "identity.otp.exhausted",
                "OTP attempts exhausted",
                "The OTP challenge has reached its maximum number of attempts."),
            _ => VerifyOtpHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.otp.invalid",
                "Invalid OTP",
                "The OTP challenge is no longer valid."),
        };
    }

    private static Guid? ResolveSubjectAccountId(System.Security.Claims.ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var parsed) ? parsed : null;
    }

    private static string NormalizeIdentifier(string identifier)
    {
        var trimmed = identifier.Trim();
        if (trimmed.Contains('@'))
        {
            return trimmed.ToLowerInvariant();
        }

        var buffer = new StringBuilder(trimmed.Length + 1);
        foreach (var ch in trimmed)
        {
            if (char.IsDigit(ch))
            {
                buffer.Append(ch);
            }
        }

        return buffer.Length == 0 ? trimmed.ToLowerInvariant() : $"+{buffer}";
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

public sealed record VerifyOtpHandlerResult(
    bool IsSuccess,
    AuthSessionResponse? Session,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail)
{
    public static VerifyOtpHandlerResult Success(AuthSessionResponse? session)
    {
        return new VerifyOtpHandlerResult(true, session, StatusCodes.Status200OK, null, null, null);
    }

    public static VerifyOtpHandlerResult Fail(
        int statusCode,
        string reasonCode,
        string title,
        string detail)
    {
        return new VerifyOtpHandlerResult(false, null, statusCode, reasonCode, title, detail);
    }
}
