using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace BackendApi.Modules.Identity.Customer.SignIn;

public static class CustomerSignInHandler
{
    private static readonly TimeSpan[] ProgressiveCooldowns =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
    ];

    private const int FailuresPerTier = 5;
    private const int AdminUnlockTier = 4;

    public static async Task<CustomerSignInHandlerResult> HandleAsync(
        CustomerSignInRequest request,
        HttpContext httpContext,
        IdentityDbContext dbContext,
        Argon2idHasher hasher,
        CustomerAuthSessionService authSessionService,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var normalizedIdentifier = request.Identifier.Trim();
        var normalizedEmail = normalizedIdentifier.ToLowerInvariant();
        var normalizedPhone = LooksLikePhoneIdentifier(normalizedIdentifier)
            ? NormalizePhoneIdentifier(normalizedIdentifier)
            : "__not-a-phone__";
        var now = DateTimeOffset.UtcNow;

        var account = await dbContext.Accounts.SingleOrDefaultAsync(
            x => x.Surface == "customer"
                 && (x.EmailNormalized == normalizedEmail || x.PhoneE164 == normalizedPhone),
            cancellationToken);

        if (account is not null)
        {
            var lockout = await dbContext.LockoutStates.SingleOrDefaultAsync(
                x => x.AccountId == account.Id && x.Reason == "signin",
                cancellationToken);

            if (lockout is not null && lockout.RequiresAdminUnlock)
            {
                return CustomerSignInHandlerResult.AdminUnlockRequired();
            }

            if (lockout is not null && lockout.LockedUntil is DateTimeOffset lockedUntil && lockedUntil > now)
            {
                return CustomerSignInHandlerResult.Locked(lockedUntil);
            }
        }

        if (account is null)
        {
            _ = hasher.HashPassword(request.Password, SurfaceKind.Customer);
            _ = await dbContext.Accounts
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Select(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: IdentityAuditActors.AnonymousActorId,
                    ActorRole: "customer",
                    Action: "customer.signin.failed",
                    EntityType: "signin_attempt",
                    EntityId: Guid.NewGuid(),
                    BeforeState: null,
                    AfterState: new { request.Identifier },
                    Reason: "identity.sign_in.invalid_credentials"),
                cancellationToken);
            return CustomerSignInHandlerResult.InvalidCredentials();
        }

        if (!string.Equals(account.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: account.Id,
                    ActorRole: "customer",
                    Action: "customer.signin.failed",
                    EntityType: nameof(Account),
                    EntityId: account.Id,
                    BeforeState: new { account.Status },
                    AfterState: null,
                    Reason: "identity.account.disabled"),
                cancellationToken);
            return CustomerSignInHandlerResult.AccountUnavailable(account.Status);
        }

        var verify = hasher.VerifyAndRehashIfNeeded(request.Password, account.PasswordHash, SurfaceKind.Customer);
        if (!verify.IsValid)
        {
            await RecordFailureAsync(dbContext, account.Id, now, auditEventPublisher, cancellationToken);
            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: account.Id,
                    ActorRole: "customer",
                    Action: "customer.signin.failed",
                    EntityType: nameof(Account),
                    EntityId: account.Id,
                    BeforeState: null,
                    AfterState: null,
                    Reason: "identity.sign_in.invalid_credentials"),
                cancellationToken);
            return CustomerSignInHandlerResult.InvalidCredentials();
        }

        if (verify.NeedsRehash && !string.IsNullOrWhiteSpace(verify.RehashedHash))
        {
            account.PasswordHash = verify.RehashedHash;
            account.UpdatedAt = now;
        }

        await ResetLockoutAsync(dbContext, account.Id, now, auditEventPublisher, cancellationToken);

        var session = authSessionService.CreateForNewSession(account, httpContext);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: account.Id,
                ActorRole: "customer",
                Action: "customer.signin.succeeded",
                EntityType: nameof(Account),
                EntityId: account.Id,
                BeforeState: null,
                AfterState: new { account.Id, session.AccessTokenExpiresAt },
                Reason: "sign_in"),
            cancellationToken);

        return CustomerSignInHandlerResult.Success(session);
    }

    private static async Task RecordFailureAsync(
        IdentityDbContext dbContext,
        Guid accountId,
        DateTimeOffset now,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var lockout = await dbContext.LockoutStates.SingleOrDefaultAsync(
            x => x.AccountId == accountId && x.Reason == "signin",
            cancellationToken);

        if (lockout is null)
        {
            dbContext.LockoutStates.Add(new LockoutState
            {
                AccountId = accountId,
                Reason = "signin",
                FailedCount = 1,
                Tier = 0,
                CooldownIndex = 0,
                RequiresAdminUnlock = false,
                FirstFailedAt = now,
                UpdatedAt = now,
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var before = new
        {
            lockout.FailedCount,
            lockout.Tier,
            lockout.CooldownIndex,
            lockout.RequiresAdminUnlock,
            lockout.LockedUntil,
        };

        if (lockout.RequiresAdminUnlock)
        {
            return;
        }

        if (lockout.LockedUntil is DateTimeOffset lockedUntil && lockedUntil > now)
        {
            return;
        }

        if (lockout.FirstFailedAt is null || now - lockout.FirstFailedAt > TimeSpan.FromMinutes(15))
        {
            lockout.FirstFailedAt = now;
            lockout.FailedCount = 1;
            lockout.LockedUntil = null;
        }
        else
        {
            lockout.FailedCount += 1;
            if (lockout.FailedCount >= FailuresPerTier)
            {
                var nextTier = Math.Min(lockout.Tier + 1, AdminUnlockTier);
                lockout.Tier = nextTier;
                lockout.CooldownIndex = nextTier;
                lockout.FailedCount = 0;
                lockout.FirstFailedAt = now;

                if (nextTier >= AdminUnlockTier)
                {
                    lockout.RequiresAdminUnlock = true;
                    lockout.LockedUntil = null;
                }
                else
                {
                    lockout.LockedUntil = now.Add(ProgressiveCooldowns[nextTier - 1]);
                }

                await auditEventPublisher.PublishAsync(
                    new AuditEvent(
                        ActorId: accountId,
                        ActorRole: "customer",
                        Action: "customer.lockout_triggered",
                        EntityType: nameof(LockoutState),
                        EntityId: accountId,
                        BeforeState: before,
                        AfterState: new
                        {
                            lockout.FailedCount,
                            lockout.Tier,
                            lockout.CooldownIndex,
                            lockout.RequiresAdminUnlock,
                            lockout.LockedUntil,
                        },
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
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var lockout = await dbContext.LockoutStates.SingleOrDefaultAsync(
            x => x.AccountId == accountId && x.Reason == "signin",
            cancellationToken);

        if (lockout is null)
        {
            return;
        }

        var hadLockoutState = lockout.FailedCount > 0
                              || lockout.Tier > 0
                              || lockout.CooldownIndex > 0
                              || lockout.RequiresAdminUnlock
                              || lockout.LockedUntil is not null;

        var before = new
        {
            lockout.FailedCount,
            lockout.Tier,
            lockout.CooldownIndex,
            lockout.RequiresAdminUnlock,
            lockout.LockedUntil,
        };

        lockout.FailedCount = 0;
        lockout.Tier = 0;
        lockout.CooldownIndex = 0;
        lockout.RequiresAdminUnlock = false;
        lockout.FirstFailedAt = null;
        lockout.LockedUntil = null;
        lockout.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        if (hadLockoutState)
        {
            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: accountId,
                    ActorRole: "customer",
                    Action: "customer.lockout_cleared",
                    EntityType: nameof(LockoutState),
                    EntityId: accountId,
                    BeforeState: before,
                    AfterState: new
                    {
                        lockout.FailedCount,
                        lockout.Tier,
                        lockout.CooldownIndex,
                        lockout.RequiresAdminUnlock,
                        lockout.LockedUntil,
                    },
                    Reason: "signin_success"),
                cancellationToken);
        }
    }

    private static bool LooksLikePhoneIdentifier(string identifier)
    {
        return !identifier.Contains('@') && identifier.Any(char.IsDigit);
    }

    private static string NormalizePhoneIdentifier(string identifier)
    {
        var buffer = new StringBuilder(identifier.Length + 1);
        foreach (var ch in identifier)
        {
            if (char.IsDigit(ch))
            {
                buffer.Append(ch);
            }
        }

        return buffer.Length == 0 ? "__not-a-phone__" : $"+{buffer}";
    }
}

public sealed record CustomerSignInHandlerResult(
    bool IsSuccess,
    AuthSessionResponse? Session,
    DateTimeOffset? LockedUntil,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail)
{
    public static CustomerSignInHandlerResult Success(AuthSessionResponse session)
    {
        return new CustomerSignInHandlerResult(true, session, null, StatusCodes.Status200OK, null, null, null);
    }

    public static CustomerSignInHandlerResult Locked(DateTimeOffset lockedUntil)
    {
        return new CustomerSignInHandlerResult(
            false,
            null,
            lockedUntil,
            StatusCodes.Status423Locked,
            "identity.lockout.active",
            "Account is temporarily locked",
            "Too many failed sign-in attempts.");
    }

    public static CustomerSignInHandlerResult InvalidCredentials()
    {
        return new CustomerSignInHandlerResult(
            false,
            null,
            null,
            StatusCodes.Status400BadRequest,
            "identity.sign_in.invalid_credentials",
            "Invalid credentials",
            "Invalid email or password.");
    }

    public static CustomerSignInHandlerResult AdminUnlockRequired()
    {
        return new CustomerSignInHandlerResult(
            false,
            null,
            null,
            StatusCodes.Status423Locked,
            "identity.lockout.admin_unlock_required",
            "Account requires administrator unlock",
            "Too many failed sign-in attempts require an administrator unlock.");
    }

    public static CustomerSignInHandlerResult AccountUnavailable(string status)
    {
        return status.Trim().ToLowerInvariant() switch
        {
            "pending_email_verification" => new CustomerSignInHandlerResult(
                false,
                null,
                null,
                StatusCodes.Status403Forbidden,
                "identity.account.pending_email_verification",
                "Email verification required",
                "Please verify your email before signing in."),
            "locked" => new CustomerSignInHandlerResult(
                false,
                null,
                null,
                StatusCodes.Status423Locked,
                "identity.account.locked",
                "Account is locked",
                "The account is currently locked."),
            "disabled" => new CustomerSignInHandlerResult(
                false,
                null,
                null,
                StatusCodes.Status403Forbidden,
                "identity.account.disabled",
                "Account is disabled",
                "The account is disabled."),
            "deleted" => new CustomerSignInHandlerResult(
                false,
                null,
                null,
                StatusCodes.Status403Forbidden,
                "identity.account.deleted",
                "Account is unavailable",
                "The account is unavailable."),
            _ => new CustomerSignInHandlerResult(
                false,
                null,
                null,
                StatusCodes.Status403Forbidden,
                "identity.account.disabled",
                "Account is unavailable",
                "The account is unavailable."),
        };
    }
}
