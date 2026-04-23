using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using BackendApi.Modules.Identity.Primitives.StateMachines;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Customer.ConfirmEmail;

public static class ConfirmEmailHandler
{
    public static async Task<ConfirmEmailHandlerResult> HandleAsync(
        ConfirmEmailRequest request,
        IdentityDbContext dbContext,
        IdentityTokenSecretHasher tokenSecretHasher,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        if (!IdentityOpaqueTokenCodec.TryParse(request.Token, out var token))
        {
            return ConfirmEmailHandlerResult.Fail(
                StatusCodes.Status409Conflict,
                "identity.email_verification.consumed",
                "Token consumed",
                "The verification token is invalid or already consumed.");
        }

        var challenge = await dbContext.EmailVerificationChallenges
            .SingleOrDefaultAsync(x => x.TokenId == token.TokenId, cancellationToken);

        if (challenge is null)
        {
            return ConfirmEmailHandlerResult.Fail(
                StatusCodes.Status409Conflict,
                "identity.email_verification.consumed",
                "Token consumed",
                "The verification token is invalid or already consumed.");
        }

        var storedHash = challenge.TokenSecretHash ?? challenge.TokenHash;
        if (storedHash is null || !tokenSecretHasher.Verify(token.Secret, storedHash))
        {
            return ConfirmEmailHandlerResult.Fail(
                StatusCodes.Status409Conflict,
                "identity.email_verification.consumed",
                "Token consumed",
                "The verification token is invalid or already consumed.");
        }

        var emailStateMachine = new EmailVerificationStateMachine();
        if (!string.Equals(challenge.Status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            return ConfirmEmailHandlerResult.Fail(
                StatusCodes.Status409Conflict,
                "identity.email_verification.consumed",
                "Token consumed",
                "The verification token has already been consumed.");
        }

        var now = DateTimeOffset.UtcNow;
        if (challenge.ExpiresAt <= now)
        {
            _ = emailStateMachine.TryTransition(
                EmailVerificationState.Pending,
                EmailVerificationTrigger.Expires,
                out _);

            challenge.Status = "expired";
            challenge.CompletedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);

            return ConfirmEmailHandlerResult.Fail(
                StatusCodes.Status410Gone,
                "identity.email_verification.expired",
                "Token expired",
                "The verification token has expired.");
        }

        _ = emailStateMachine.TryTransition(
            EmailVerificationState.Pending,
            EmailVerificationTrigger.Confirm,
            out _);
        challenge.Status = "completed";
        challenge.CompletedAt = now;

        var account = await dbContext.Accounts.SingleOrDefaultAsync(x => x.Id == challenge.AccountId, cancellationToken);
        if (account is not null)
        {
            var accountStateMachine = new AccountStateMachine();
            if (TryParseAccountState(account.Status, out var accountState)
                && accountStateMachine.TryTransition(accountState, AccountTrigger.ConfirmEmail, out var nextAccountState))
            {
                account.Status = MapAccountState(nextAccountState);
            }

            account.EmailVerifiedAt = now;
            account.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (account is not null)
        {
            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: account.Id,
                    ActorRole: "customer",
                    Action: "email.verified",
                    EntityType: nameof(Account),
                    EntityId: account.Id,
                    BeforeState: null,
                    AfterState: new
                    {
                        account.Id,
                        account.Status,
                        account.EmailVerifiedAt,
                    },
                    Reason: "customer.email.confirm"),
                cancellationToken);
        }

        return ConfirmEmailHandlerResult.Success();
    }

    private static bool TryParseAccountState(string status, out AccountState state)
    {
        switch (status.Trim().ToLowerInvariant())
        {
            case "pending_email_verification":
                state = AccountState.PendingEmailVerification;
                return true;
            case "active":
                state = AccountState.Active;
                return true;
            case "locked":
                state = AccountState.Locked;
                return true;
            case "disabled":
                state = AccountState.Disabled;
                return true;
            case "deleted":
                state = AccountState.Deleted;
                return true;
            default:
                state = AccountState.PendingEmailVerification;
                return false;
        }
    }

    private static string MapAccountState(AccountState state)
    {
        return state switch
        {
            AccountState.PendingEmailVerification => "pending_email_verification",
            AccountState.Active => "active",
            AccountState.Locked => "locked",
            AccountState.Disabled => "disabled",
            AccountState.Deleted => "deleted",
            _ => "pending_email_verification",
        };
    }
}

public sealed record ConfirmEmailHandlerResult(
    bool IsSuccess,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail)
{
    public static ConfirmEmailHandlerResult Success()
    {
        return new ConfirmEmailHandlerResult(true, StatusCodes.Status200OK, null, null, null);
    }

    public static ConfirmEmailHandlerResult Fail(
        int statusCode,
        string reasonCode,
        string title,
        string detail)
    {
        return new ConfirmEmailHandlerResult(false, statusCode, reasonCode, title, detail);
    }
}
