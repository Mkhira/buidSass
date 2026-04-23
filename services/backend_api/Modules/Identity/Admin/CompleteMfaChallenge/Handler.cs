using System.Security.Cryptography;
using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Admin.EnrollTotp;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OtpNet;

namespace BackendApi.Modules.Identity.Admin.CompleteMfaChallenge;

public static class CompleteMfaChallengeHandler
{
    public static async Task<CompleteMfaChallengeHandlerResult> HandleAsync(
        CompleteMfaChallengeRequest request,
        HttpContext httpContext,
        IdentityDbContext dbContext,
        AdminMfaChallengeStore mfaChallengeStore,
        IDataProtectionProvider dataProtectionProvider,
        Argon2idHasher hasher,
        AdminAuthSessionService authSessionService,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var challengeLookup = await mfaChallengeStore.TryGetAsync(request.ChallengeId, cancellationToken);
        if (challengeLookup.Status != MfaChallengeLookupStatus.Active || challengeLookup.ChallengeState is null)
        {
            if (challengeLookup.Status == MfaChallengeLookupStatus.Exhausted)
            {
                await auditEventPublisher.PublishAsync(
                    new AuditEvent(
                        ActorId: IdentityAuditActors.AnonymousActorId,
                        ActorRole: "admin",
                        Action: "admin.mfa.verification_failed",
                        EntityType: "mfa_challenge",
                        EntityId: request.ChallengeId,
                        BeforeState: null,
                        AfterState: null,
                        Reason: "identity.mfa.challenge_exhausted"),
                    cancellationToken);
                return CompleteMfaChallengeHandlerResult.Fail(
                    StatusCodes.Status400BadRequest,
                    "identity.mfa.challenge_exhausted",
                    "MFA challenge exhausted",
                    "Too many invalid MFA attempts. Start sign-in again.");
            }

            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: IdentityAuditActors.AnonymousActorId,
                    ActorRole: "admin",
                    Action: "admin.mfa.verification_failed",
                    EntityType: "mfa_challenge",
                    EntityId: request.ChallengeId,
                    BeforeState: null,
                    AfterState: null,
                    Reason: "identity.mfa.challenge_invalid"),
                cancellationToken);
            return CompleteMfaChallengeHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.mfa.challenge_invalid",
                "Invalid MFA challenge",
                "The MFA challenge is invalid or expired.");
        }

        var challengeState = challengeLookup.ChallengeState.Value;

        var factor = await dbContext.AdminMfaFactors.SingleOrDefaultAsync(
            x => x.Id == challengeState.FactorId
                && x.AccountId == challengeState.AccountId
                && x.Kind == "totp"
                && x.ConfirmedAt != null
                && x.RevokedAt == null,
            cancellationToken);

        if (factor is null)
        {
            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: challengeState.AccountId,
                    ActorRole: "admin",
                    Action: "admin.mfa.verification_failed",
                    EntityType: "mfa_factor",
                    EntityId: challengeState.FactorId,
                    BeforeState: null,
                    AfterState: null,
                    Reason: "identity.mfa.factor_not_found"),
                cancellationToken);
            return CompleteMfaChallengeHandlerResult.Fail(
                StatusCodes.Status404NotFound,
                "identity.mfa.factor_not_found",
                "MFA factor not found",
                "The target MFA factor was not found.");
        }

        var valid = false;
        var windowCounter = 0L;
        var totpUnavailable = false;

        if (request.Code.Length == 6)
        {
            var protector = dataProtectionProvider.CreateProtector("identity.admin.totp.secret.v1");
            try
            {
                var secretBytes = TotpSecretCodec.Decode(protector, factor.SecretEncrypted);
                var totp = new Totp(secretBytes);
                valid = totp.VerifyTotp(
                    request.Code,
                    out windowCounter,
                    VerificationWindow.RfcSpecifiedNetworkDelay);
            }
            catch (TotpSecretUnprotectFailed)
            {
                totpUnavailable = true;
            }
        }

        if (!valid)
        {
            if (TryConsumeRecoveryCode(request.Code, factor, hasher))
            {
                factor.LastUsedAt = DateTimeOffset.UtcNow;

                var recoveryAccount = await dbContext.Accounts.SingleAsync(
                    x => x.Id == challengeState.AccountId,
                    cancellationToken);

                var recoverySession = await authSessionService.IssueAdminSessionAsync(recoveryAccount, httpContext, cancellationToken);
                await mfaChallengeStore.ConsumeAsync(challengeState.ChallengeId, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);

                await auditEventPublisher.PublishAsync(
                    new AuditEvent(
                        ActorId: challengeState.AccountId,
                        ActorRole: "admin",
                        Action: "admin.mfa.recovery_code_consumed",
                        EntityType: nameof(AdminMfaFactor),
                        EntityId: factor.Id,
                        BeforeState: null,
                        AfterState: new { SessionId = recoverySession.Session.Id, factor.LastUsedAt },
                        Reason: "mfa_challenge"),
                    cancellationToken);

                await auditEventPublisher.PublishAsync(
                    new AuditEvent(
                        ActorId: challengeState.AccountId,
                        ActorRole: "admin",
                        Action: "admin.mfa.verification_succeeded",
                        EntityType: nameof(AdminMfaFactor),
                        EntityId: factor.Id,
                        BeforeState: null,
                        AfterState: new { SessionId = recoverySession.Session.Id, factor.LastUsedAt, Method = "recovery_code" },
                        Reason: "mfa_challenge"),
                    cancellationToken);

                return CompleteMfaChallengeHandlerResult.Success(recoverySession);
            }

            if (totpUnavailable)
            {
                await auditEventPublisher.PublishAsync(
                    new AuditEvent(
                        ActorId: challengeState.AccountId,
                        ActorRole: "admin",
                        Action: "admin.mfa.verification_failed",
                        EntityType: nameof(AdminMfaFactor),
                        EntityId: factor.Id,
                        BeforeState: null,
                        AfterState: null,
                        Reason: "identity.mfa.secret_unprotect_failed"),
                    cancellationToken);
                return CompleteMfaChallengeHandlerResult.Fail(
                    StatusCodes.Status503ServiceUnavailable,
                    "identity.mfa.secret_unprotect_failed",
                    "MFA factor unavailable",
                    "MFA verification is temporarily unavailable. Contact support.");
            }

            var attemptResult = await mfaChallengeStore.RegisterFailedAttemptAsync(request.ChallengeId, cancellationToken);
            if (attemptResult.IsExhausted)
            {
                await auditEventPublisher.PublishAsync(
                    new AuditEvent(
                        ActorId: challengeState.AccountId,
                        ActorRole: "admin",
                        Action: "admin.mfa.verification_failed",
                        EntityType: nameof(AdminMfaFactor),
                        EntityId: factor.Id,
                        BeforeState: null,
                        AfterState: new { attemptResult.Attempts, attemptResult.IsExhausted },
                        Reason: "identity.mfa.challenge_exhausted"),
                    cancellationToken);
                return CompleteMfaChallengeHandlerResult.Fail(
                    StatusCodes.Status400BadRequest,
                    "identity.mfa.challenge_exhausted",
                    "MFA challenge exhausted",
                    "Too many invalid MFA attempts. Start sign-in again.");
            }

            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: challengeState.AccountId,
                    ActorRole: "admin",
                    Action: "admin.mfa.verification_failed",
                    EntityType: nameof(AdminMfaFactor),
                    EntityId: factor.Id,
                    BeforeState: null,
                    AfterState: new { attemptResult.Attempts, attemptResult.IsExhausted },
                    Reason: "identity.mfa.invalid_code"),
                cancellationToken);
            return CompleteMfaChallengeHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.mfa.invalid_code",
                "Invalid MFA code",
                "The provided MFA code is invalid.");
        }

        var replayExists = await dbContext.AdminMfaReplayGuards.AnyAsync(
            x => x.FactorId == factor.Id && x.WindowCounter == windowCounter,
            cancellationToken);

        if (replayExists)
        {
            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: challengeState.AccountId,
                    ActorRole: "admin",
                    Action: "admin.mfa.verification_failed",
                    EntityType: nameof(AdminMfaFactor),
                    EntityId: factor.Id,
                    BeforeState: null,
                    AfterState: new { windowCounter },
                    Reason: "identity.mfa.replay"),
                cancellationToken);
            return CompleteMfaChallengeHandlerResult.Fail(
                StatusCodes.Status409Conflict,
                "identity.mfa.replay",
                "MFA replay detected",
                "The MFA code was already used in this time window.");
        }

        dbContext.AdminMfaReplayGuards.Add(new AdminMfaReplayGuard
        {
            FactorId = factor.Id,
            WindowCounter = windowCounter,
            ObservedAt = DateTimeOffset.UtcNow,
        });

        factor.LastUsedAt = DateTimeOffset.UtcNow;

        var account = await dbContext.Accounts.SingleAsync(
            x => x.Id == challengeState.AccountId,
            cancellationToken);

        var session = await authSessionService.IssueAdminSessionAsync(account, httpContext, cancellationToken);
        await mfaChallengeStore.ConsumeAsync(challengeState.ChallengeId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: challengeState.AccountId,
                ActorRole: "admin",
                Action: "admin.mfa.verification_succeeded",
                EntityType: nameof(AdminMfaFactor),
                EntityId: factor.Id,
                BeforeState: null,
                AfterState: new { SessionId = session.Session.Id, factor.LastUsedAt },
                Reason: "mfa_challenge"),
            cancellationToken);

        return CompleteMfaChallengeHandlerResult.Success(session);
    }

    private static bool TryConsumeRecoveryCode(string code, AdminMfaFactor factor, Argon2idHasher hasher)
    {
        var recoveryCodes = DeserializeRecoveryCodes(factor.RecoveryCodesHash);
        if (recoveryCodes.Count == 0)
        {
            return false;
        }

        var requestedCodeLegacyHash = AdminIdentityResponseFactory.HashString(code);
        var matchedIndex = -1;
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < recoveryCodes.Count; i++)
        {
            var entry = recoveryCodes[i];
            var isMatch = VerifyRecoveryCodeHash(code, requestedCodeLegacyHash, entry.Hash, hasher);

            if (entry.UsedAt is null && isMatch && matchedIndex < 0)
            {
                matchedIndex = i;
            }
        }

        if (matchedIndex < 0)
        {
            return false;
        }

        recoveryCodes[matchedIndex] = recoveryCodes[matchedIndex] with { UsedAt = now };
        factor.RecoveryCodesHash = JsonSerializer.Serialize(recoveryCodes);
        return true;
    }

    private static List<RecoveryCodeHashPayload> DeserializeRecoveryCodes(string serializedCodes)
    {
        if (string.IsNullOrWhiteSpace(serializedCodes))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<RecoveryCodeHashPayload>>(serializedCodes) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool VerifyRecoveryCodeHash(
        string code,
        byte[] requestedCodeLegacyHash,
        string storedHash,
        Argon2idHasher hasher)
    {
        if (storedHash.StartsWith("$argon2", StringComparison.Ordinal))
        {
            try
            {
                return hasher.VerifyAndRehashIfNeeded(code, storedHash, SurfaceKind.Admin).IsValid;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        try
        {
            var legacyHash = Convert.FromBase64String(storedHash);
            return CryptographicOperations.FixedTimeEquals(legacyHash, requestedCodeLegacyHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed record CompleteMfaChallengeHandlerResult(
    bool IsSuccess,
    AdminAuthSessionResponse? Session,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail)
{
    public static CompleteMfaChallengeHandlerResult Success(AdminAuthSessionResponse session)
    {
        return new CompleteMfaChallengeHandlerResult(true, session, StatusCodes.Status200OK, null, null, null);
    }

    public static CompleteMfaChallengeHandlerResult Fail(int statusCode, string reasonCode, string title, string detail)
    {
        return new CompleteMfaChallengeHandlerResult(false, null, statusCode, reasonCode, title, detail);
    }
}
