using System.Security.Claims;
using System.Security.Cryptography;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Admin.CompleteStepUpOtp;

public static class CompleteStepUpOtpHandler
{
    public static async Task<CompleteStepUpOtpHandlerResult> HandleAsync(
        ClaimsPrincipal user,
        CompleteStepUpOtpRequest request,
        IdentityDbContext dbContext,
        IJwtIssuer jwtIssuer,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var subject = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(subject, out var accountId))
        {
            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: IdentityAuditActors.AnonymousActorId,
                    ActorRole: "admin",
                    Action: "admin.stepup.failed",
                    EntityType: "stepup_request",
                    EntityId: Guid.NewGuid(),
                    BeforeState: null,
                    AfterState: null,
                    Reason: "identity.common.denied"),
                cancellationToken);
            return CompleteStepUpOtpHandlerResult.Fail(
                StatusCodes.Status401Unauthorized,
                "identity.common.denied",
                "Unauthorized",
                "Authentication is required.");
        }

        var challenge = await dbContext.OtpChallenges.SingleOrDefaultAsync(
            x => x.Id == request.ChallengeId
                 && x.AccountId == accountId
                 && x.Surface == "admin",
            cancellationToken);

        if (challenge is null)
        {
            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: accountId,
                    ActorRole: "admin",
                    Action: "admin.stepup.failed",
                    EntityType: nameof(OtpChallenge),
                    EntityId: request.ChallengeId,
                    BeforeState: null,
                    AfterState: null,
                    Reason: "identity.mfa.challenge_invalid"),
                cancellationToken);
            return CompleteStepUpOtpHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.mfa.challenge_invalid",
                "Invalid step-up challenge",
                "The step-up challenge is invalid or expired.");
        }

        var now = DateTimeOffset.UtcNow;

        if (!string.Equals(challenge.Status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: accountId,
                    ActorRole: "admin",
                    Action: "admin.stepup.failed",
                    EntityType: nameof(OtpChallenge),
                    EntityId: challenge.Id,
                    BeforeState: new { challenge.Status },
                    AfterState: null,
                    Reason: "identity.mfa.challenge_invalid"),
                cancellationToken);
            return CompleteStepUpOtpHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.mfa.challenge_invalid",
                "Invalid step-up challenge",
                "The step-up challenge is invalid or expired.");
        }

        if (challenge.ExpiresAt <= now)
        {
            challenge.Status = "expired";
            challenge.CompletedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: accountId,
                    ActorRole: "admin",
                    Action: "admin.stepup.failed",
                    EntityType: nameof(OtpChallenge),
                    EntityId: challenge.Id,
                    BeforeState: null,
                    AfterState: new { challenge.Status, challenge.CompletedAt },
                    Reason: "identity.otp.expired"),
                cancellationToken);

            return CompleteStepUpOtpHandlerResult.Fail(
                StatusCodes.Status410Gone,
                "identity.otp.expired",
                "Step-up OTP expired",
                "The step-up OTP has expired.");
        }

        var submittedHash = AdminIdentityResponseFactory.HashString(request.Code);
        if (!CryptographicOperations.FixedTimeEquals(submittedHash, challenge.CodeHash))
        {
            challenge.Attempts += 1;
            if (challenge.Attempts >= challenge.MaxAttempts)
            {
                challenge.Status = "exhausted";
                challenge.CompletedAt = now;
                await dbContext.SaveChangesAsync(cancellationToken);

                await auditEventPublisher.PublishAsync(
                    new AuditEvent(
                        ActorId: accountId,
                        ActorRole: "admin",
                        Action: "admin.stepup.failed",
                        EntityType: nameof(OtpChallenge),
                        EntityId: challenge.Id,
                        BeforeState: null,
                        AfterState: new { challenge.Status, challenge.Attempts, challenge.CompletedAt },
                        Reason: "identity.otp.exhausted"),
                    cancellationToken);

                return CompleteStepUpOtpHandlerResult.Fail(
                    StatusCodes.Status429TooManyRequests,
                    "identity.otp.exhausted",
                    "Step-up OTP attempts exhausted",
                    "The step-up challenge has reached its maximum number of attempts.");
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: accountId,
                    ActorRole: "admin",
                    Action: "admin.stepup.failed",
                    EntityType: nameof(OtpChallenge),
                    EntityId: challenge.Id,
                    BeforeState: null,
                    AfterState: new { challenge.Attempts },
                    Reason: "identity.otp.invalid"),
                cancellationToken);

            return CompleteStepUpOtpHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.otp.invalid",
                "Invalid step-up OTP",
                "The step-up OTP code is invalid.");
        }

        challenge.Status = "completed";
        challenge.CompletedAt = now;

        var stepUpValidUntil = now.AddMinutes(10);
        var claims = new List<Claim>
        {
            new("market_code", user.FindFirstValue("market_code") ?? "platform"),
            new("step_up_valid_until", stepUpValidUntil.ToString("O")),
        };

        var sid = user.FindFirstValue("sid");
        if (!string.IsNullOrWhiteSpace(sid))
        {
            claims.Add(new Claim("sid", sid));
        }

        var permissionVersion = user.FindFirstValue("permission_version");
        if (!string.IsNullOrWhiteSpace(permissionVersion))
        {
            claims.Add(new Claim("permission_version", permissionVersion));
        }

        var token = jwtIssuer.IssueAccessToken(
            new JwtIssueRequest(
                SurfaceKind.Admin,
                accountId.ToString(),
                claims));

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: accountId,
                ActorRole: "admin",
                Action: "admin.stepup.passed",
                EntityType: nameof(OtpChallenge),
                EntityId: challenge.Id,
                BeforeState: null,
                AfterState: new { challenge.CompletedAt, StepUpValidUntil = stepUpValidUntil },
                Reason: "step_up"),
            cancellationToken);
        return CompleteStepUpOtpHandlerResult.Success(token.AccessToken, token.ExpiresAt, stepUpValidUntil);
    }
}

public sealed record CompleteStepUpOtpHandlerResult(
    bool IsSuccess,
    string? AccessToken,
    DateTimeOffset? AccessTokenExpiresAt,
    DateTimeOffset? StepUpValidUntil,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail)
{
    public static CompleteStepUpOtpHandlerResult Success(
        string accessToken,
        DateTimeOffset accessTokenExpiresAt,
        DateTimeOffset stepUpValidUntil) =>
        new(true, accessToken, accessTokenExpiresAt, stepUpValidUntil, StatusCodes.Status200OK, null, null, null);

    public static CompleteStepUpOtpHandlerResult Fail(int statusCode, string reasonCode, string title, string detail) =>
        new(false, null, null, null, statusCode, reasonCode, title, detail);
}
