using System.Security.Claims;
using System.Security.Cryptography;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Admin.StepUpOtp;

public static class StepUpOtpHandler
{
    public static async Task<StepUpOtpHandlerResult> HandleAsync(
        ClaimsPrincipal user,
        HttpContext context,
        StepUpOtpRequest request,
        IdentityDbContext dbContext,
        IOtpChallengeDispatcher dispatcher,
        IdentityClientSecurityHasher clientSecurityHasher,
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
            return StepUpOtpHandlerResult.Fail(
                StatusCodes.Status401Unauthorized,
                "identity.common.denied",
                "Unauthorized",
                "Authentication is required.");
        }

        var account = await dbContext.Accounts.SingleOrDefaultAsync(
            x => x.Id == accountId && x.Surface == "admin",
            cancellationToken);

        if (account is null)
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
            return StepUpOtpHandlerResult.Fail(
                StatusCodes.Status401Unauthorized,
                "identity.common.denied",
                "Unauthorized",
                "Authentication is required.");
        }

        var code = CreateNumericOtpCode(8);
        var now = DateTimeOffset.UtcNow;
        var challenge = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Purpose = $"admin_step_up:{request.Purpose.Trim().ToLowerInvariant()}",
            Surface = "admin",
            Channel = "sms",
            DestinationHash = clientSecurityHasher.HashIdentifier(account.EmailNormalized),
            CodeHash = AdminIdentityResponseFactory.HashString(code),
            CodeLength = 8,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(3),
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
                    SurfaceKind.Admin,
                    account.EmailDisplay,
                    challenge.Purpose,
                    code,
                    AdminIdentityResponseFactory.ResolveCorrelationId(context)),
                cancellationToken);
        }
        catch (IdentityDeliveryNotConfiguredException)
        {
            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: account.Id,
                    ActorRole: "admin",
                    Action: "admin.stepup.failed",
                    EntityType: nameof(OtpChallenge),
                    EntityId: challenge.Id,
                    BeforeState: null,
                    AfterState: null,
                    Reason: "identity.otp.dispatch_unavailable"),
                cancellationToken);
            return StepUpOtpHandlerResult.Fail(
                StatusCodes.Status503ServiceUnavailable,
                "identity.otp.dispatch_unavailable",
                "OTP service unavailable",
                "OTP delivery is temporarily unavailable.");
        }

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: account.Id,
                ActorRole: "admin",
                Action: "admin.stepup.issued",
                EntityType: nameof(OtpChallenge),
                EntityId: challenge.Id,
                BeforeState: null,
                AfterState: new { challenge.Purpose, challenge.ExpiresAt },
                Reason: "step_up"),
            cancellationToken);

        return StepUpOtpHandlerResult.Success(challenge.Id);
    }

    private static string CreateNumericOtpCode(int digits)
    {
        // Rejection sampling to avoid modulo bias: accept only bytes in [0, 250).
        const byte acceptanceThreshold = 250;
        var chars = new char[digits];
        Span<byte> buffer = stackalloc byte[1];
        for (var i = 0; i < digits; i++)
        {
            do
            {
                RandomNumberGenerator.Fill(buffer);
            } while (buffer[0] >= acceptanceThreshold);

            chars[i] = (char)('0' + (buffer[0] % 10));
        }

        return new string(chars);
    }
}

public sealed record StepUpOtpHandlerResult(
    bool IsSuccess,
    Guid ChallengeId,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail)
{
    public static StepUpOtpHandlerResult Success(Guid challengeId) =>
        new(true, challengeId, StatusCodes.Status202Accepted, null, null, null);

    public static StepUpOtpHandlerResult Fail(int statusCode, string reasonCode, string title, string detail) =>
        new(false, Guid.Empty, statusCode, reasonCode, title, detail);
}
