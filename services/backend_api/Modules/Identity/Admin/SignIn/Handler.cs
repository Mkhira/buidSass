using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Identity.Admin.SignIn;

public static class AdminSignInHandler
{
    public static async Task<AdminSignInHandlerResult> HandleAsync(
        AdminSignInRequest request,
        HttpContext httpContext,
        IdentityDbContext dbContext,
        Argon2idHasher hasher,
        AdminMfaChallengeStore mfaChallengeStore,
        IOptions<IdentityMfaOptions> mfaOptions,
        AdminAuthSessionService authSessionService,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;

        var account = await dbContext.Accounts.SingleOrDefaultAsync(
            x => x.Surface == "admin" && x.EmailNormalized == normalizedEmail,
            cancellationToken);

        if (account is not null)
        {
            var lockout = await dbContext.LockoutStates.SingleOrDefaultAsync(
                x => x.AccountId == account.Id && x.Reason == "signin_admin",
                cancellationToken);

            if (lockout is not null && lockout.LockedUntil is DateTimeOffset lockedUntil && lockedUntil > now)
            {
                return AdminSignInHandlerResult.Locked(lockedUntil);
            }
        }

        if (account is null)
        {
            _ = hasher.HashPassword(request.Password, SurfaceKind.Admin);
            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: IdentityAuditActors.AnonymousActorId,
                    ActorRole: "admin",
                    Action: "admin.signin.failed",
                    EntityType: "signin_attempt",
                    EntityId: Guid.NewGuid(),
                    BeforeState: null,
                    AfterState: new { request.Email },
                    Reason: "identity.sign_in.invalid_credentials"),
                cancellationToken);
            return AdminSignInHandlerResult.InvalidCredentials();
        }

        if (account.Status.Equals("pending_password_rotation", StringComparison.OrdinalIgnoreCase))
        {
            return AdminSignInHandlerResult.PasswordRotationRequired();
        }

        if (!string.Equals(account.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: account.Id,
                    ActorRole: "admin",
                    Action: "admin.signin.failed",
                    EntityType: nameof(Account),
                    EntityId: account.Id,
                    BeforeState: new { account.Status },
                    AfterState: null,
                    Reason: "identity.account.disabled"),
                cancellationToken);
            return AdminSignInHandlerResult.AccountUnavailable(account.Status);
        }

        var verifyResult = hasher.VerifyAndRehashIfNeeded(request.Password, account.PasswordHash, SurfaceKind.Admin);
        if (!verifyResult.IsValid)
        {
            await RecordFailedAttemptAsync(dbContext, account.Id, now, auditEventPublisher, cancellationToken);
            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: account.Id,
                    ActorRole: "admin",
                    Action: "admin.signin.failed",
                    EntityType: nameof(Account),
                    EntityId: account.Id,
                    BeforeState: null,
                    AfterState: null,
                    Reason: "identity.sign_in.invalid_credentials"),
                cancellationToken);
            return AdminSignInHandlerResult.InvalidCredentials();
        }

        if (verifyResult.NeedsRehash && !string.IsNullOrWhiteSpace(verifyResult.RehashedHash))
        {
            account.PasswordHash = verifyResult.RehashedHash;
            account.UpdatedAt = now;
        }

        await ResetLockoutAsync(dbContext, account.Id, now, cancellationToken);

        var roleCodes = await (
                from accountRole in dbContext.AccountRoles
                join role in dbContext.Roles on accountRole.RoleId equals role.Id
                where accountRole.AccountId == account.Id
                select role.Code)
            .Distinct()
            .ToListAsync(cancellationToken);

        var requiredRoles = new HashSet<string>(
            mfaOptions.Value.RequiredRoles ?? [],
            StringComparer.OrdinalIgnoreCase);
        var isMfaRequiredTier = roleCodes.Any(requiredRoles.Contains);

        var activeFactor = await dbContext.AdminMfaFactors
            .Where(x => x.AccountId == account.Id && x.Kind == "totp" && x.ConfirmedAt != null && x.RevokedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (isMfaRequiredTier && activeFactor is null)
        {
            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: account.Id,
                    ActorRole: "admin",
                    Action: "admin.signin.failed",
                    EntityType: nameof(Account),
                    EntityId: account.Id,
                    BeforeState: null,
                    AfterState: null,
                    Reason: "identity.mfa.enrollment_required"),
                cancellationToken);
            return AdminSignInHandlerResult.MfaEnrollmentRequired();
        }

        if (isMfaRequiredTier && activeFactor is not null)
        {
            var challengeId = await mfaChallengeStore.IssueAsync(
                account.Id,
                activeFactor.Id,
                TimeSpan.FromMinutes(5),
                cancellationToken);
            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: account.Id,
                    ActorRole: "admin",
                    Action: "admin.signin.succeeded",
                    EntityType: nameof(Account),
                    EntityId: account.Id,
                    BeforeState: null,
                    AfterState: new { ChallengeId = challengeId, RequiresMfa = true },
                    Reason: "sign_in.credentials_valid"),
                cancellationToken);
            return AdminSignInHandlerResult.MfaRequired(challengeId);
        }

        var authSession = await authSessionService.IssueAdminSessionAsync(account, httpContext, cancellationToken);
        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: account.Id,
                ActorRole: "admin",
                Action: "admin.signin.succeeded",
                EntityType: nameof(Session),
                EntityId: authSession.Session.Id,
                BeforeState: null,
                AfterState: new { SessionId = authSession.Session.Id, AccountId = account.Id },
                Reason: "sign_in"),
            cancellationToken);
        return AdminSignInHandlerResult.Authenticated(authSession);
    }

    private static async Task RecordFailedAttemptAsync(
        IdentityDbContext dbContext,
        Guid accountId,
        DateTimeOffset now,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var lockout = await dbContext.LockoutStates.SingleOrDefaultAsync(
            x => x.AccountId == accountId && x.Reason == "signin_admin",
            cancellationToken);

        if (lockout is null)
        {
            lockout = new LockoutState
            {
                AccountId = accountId,
                Reason = "signin_admin",
                FailedCount = 1,
                FirstFailedAt = now,
                UpdatedAt = now,
            };
            dbContext.LockoutStates.Add(lockout);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var before = new
        {
            lockout.FailedCount,
            lockout.LockedUntil,
        };

        if (lockout.FirstFailedAt is null || now - lockout.FirstFailedAt > TimeSpan.FromMinutes(15))
        {
            lockout.FirstFailedAt = now;
            lockout.FailedCount = 1;
            lockout.LockedUntil = null;
        }
        else
        {
            lockout.FailedCount += 1;
            if (lockout.FailedCount >= 3)
            {
                lockout.LockedUntil = now.AddMinutes(30);
                await auditEventPublisher.PublishAsync(
                    new AuditEvent(
                        ActorId: accountId,
                        ActorRole: "admin",
                        Action: "admin.lockout_triggered",
                        EntityType: nameof(LockoutState),
                        EntityId: accountId,
                        BeforeState: before,
                        AfterState: new { lockout.FailedCount, lockout.LockedUntil },
                        Reason: "signin_lockout"),
                    cancellationToken);
            }
        }

        lockout.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task ResetLockoutAsync(
        IdentityDbContext dbContext,
        Guid accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var lockout = await dbContext.LockoutStates.SingleOrDefaultAsync(
            x => x.AccountId == accountId && x.Reason == "signin_admin",
            cancellationToken);

        if (lockout is null)
        {
            return;
        }

        lockout.FailedCount = 0;
        lockout.FirstFailedAt = null;
        lockout.LockedUntil = null;
        lockout.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed record AdminSignInHandlerResult(
    bool IsSuccess,
    bool IsMfaRequired,
    bool IsLocked,
    Guid? ChallengeId,
    DateTimeOffset? LockedUntil,
    AdminAuthSessionResponse? AuthSession,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail,
    IReadOnlyDictionary<string, object?>? Extensions)
{
    public static AdminSignInHandlerResult MfaRequired(Guid challengeId)
    {
        return new AdminSignInHandlerResult(
            true,
            true,
            false,
            challengeId,
            null,
            null,
            StatusCodes.Status200OK,
            null,
            null,
            null,
            null);
    }

    public static AdminSignInHandlerResult Authenticated(AdminAuthSessionResponse session)
    {
        return new AdminSignInHandlerResult(
            true,
            false,
            false,
            null,
            null,
            session,
            StatusCodes.Status200OK,
            null,
            null,
            null,
            null);
    }

    public static AdminSignInHandlerResult MfaEnrollmentRequired()
    {
        return new AdminSignInHandlerResult(
            false,
            false,
            false,
            null,
            null,
            null,
            StatusCodes.Status412PreconditionFailed,
            "identity.mfa.enrollment_required",
            "MFA enrollment required",
            "A confirmed MFA factor is required for this account role.",
            new Dictionary<string, object?>
            {
                ["enrollmentPath"] = "/v1/admin/identity/mfa/totp/enroll",
            });
    }

    public static AdminSignInHandlerResult PasswordRotationRequired()
    {
        return new AdminSignInHandlerResult(
            false,
            false,
            false,
            null,
            null,
            null,
            StatusCodes.Status412PreconditionFailed,
            "identity.password.rotation_required",
            "Password rotation required",
            "The account must rotate its password before sign-in can continue.",
            new Dictionary<string, object?>
            {
                ["rotationPath"] = "/v1/admin/identity/password/rotate",
            });
    }

    public static AdminSignInHandlerResult Locked(DateTimeOffset lockedUntil)
    {
        return new AdminSignInHandlerResult(
            false,
            false,
            true,
            null,
            lockedUntil,
            null,
            StatusCodes.Status423Locked,
            "identity.lockout.active",
            "Account is locked",
            "Too many failed sign-in attempts.",
            new Dictionary<string, object?> { ["lockedUntil"] = lockedUntil });
    }

    public static AdminSignInHandlerResult InvalidCredentials()
    {
        return new AdminSignInHandlerResult(
            false,
            false,
            false,
            null,
            null,
            null,
            StatusCodes.Status400BadRequest,
            "identity.sign_in.invalid_credentials",
            "Invalid credentials",
            "Invalid email or password.",
            null);
    }

    public static AdminSignInHandlerResult AccountUnavailable(string status)
    {
        return status.Trim().ToLowerInvariant() switch
        {
            "pending_email_verification" => new AdminSignInHandlerResult(
                false,
                false,
                false,
                null,
                null,
                null,
                StatusCodes.Status403Forbidden,
                "identity.account.pending_email_verification",
                "Email verification required",
                "The account must verify email before sign-in can continue.",
                null),
            "locked" => new AdminSignInHandlerResult(
                false,
                false,
                true,
                null,
                null,
                null,
                StatusCodes.Status423Locked,
                "identity.account.locked",
                "Account is locked",
                "The account is currently locked.",
                null),
            "disabled" => new AdminSignInHandlerResult(
                false,
                false,
                false,
                null,
                null,
                null,
                StatusCodes.Status403Forbidden,
                "identity.account.disabled",
                "Account is disabled",
                "The account is disabled.",
                null),
            "deleted" => new AdminSignInHandlerResult(
                false,
                false,
                false,
                null,
                null,
                null,
                StatusCodes.Status403Forbidden,
                "identity.account.deleted",
                "Account is unavailable",
                "The account is unavailable.",
                null),
            _ => new AdminSignInHandlerResult(
                false,
                false,
                false,
                null,
                null,
                null,
                StatusCodes.Status403Forbidden,
                "identity.account.disabled",
                "Account is unavailable",
                "The account is unavailable.",
                null),
        };
    }
}
