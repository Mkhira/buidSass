using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace BackendApi.Modules.Identity.Customer.Register;

public static class RegisterHandler
{
    private static readonly Guid AnonymousActorId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static async Task<RegisterHandlerResult> HandleAsync(
        RegisterRequest request,
        IdentityDbContext dbContext,
        Argon2idHasher hasher,
        BreachListChecker breachListChecker,
        PhoneNormalizer phoneNormalizer,
        IdentityTokenSecretHasher tokenSecretHasher,
        IIdentityEmailDispatcher emailDispatcher,
        string correlationId,
        string? actorIpHash,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var emailNormalized = email.ToLowerInvariant();

        if (breachListChecker.IsCompromised(request.Password))
        {
            return RegisterHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.register.password_too_weak",
                "Weak password",
                "The password appears in a known breached-password list.");
        }

        MarketCode marketCode;
        try
        {
            marketCode = new MarketCode(request.MarketCode);
        }
        catch (ArgumentException)
        {
            return RegisterHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.register.market_required",
                "Invalid market",
                "A valid market code is required.");
        }

        NormalizedPhone normalizedPhone;
        try
        {
            normalizedPhone = phoneNormalizer.Normalize(request.Phone, marketCode);
        }
        catch (InvalidOperationException ex)
        {
            var mismatch = ex.Message.Contains("market", StringComparison.OrdinalIgnoreCase);
            return RegisterHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                mismatch ? "identity.phone.market_mismatch" : "identity.register.invalid_phone",
                mismatch ? "Phone/market mismatch" : "Invalid phone number",
                ex.Message);
        }

        var now = DateTimeOffset.UtcNow;
        var existingAccount = await dbContext.Accounts.SingleOrDefaultAsync(
            x => x.Surface == "customer"
                && (x.EmailNormalized == emailNormalized || x.PhoneE164 == normalizedPhone.E164),
            cancellationToken);

        if (existingAccount is not null)
        {
            _ = hasher.HashPassword(request.Password, SurfaceKind.Customer);
            _ = RandomNumberGenerator.GetBytes(128);
            var targetIdentifierHash = CustomerIdentityResponseFactory.HashString(emailNormalized);

            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: AnonymousActorId,
                    ActorRole: "anonymous",
                    Action: "account.register.duplicate_rejected",
                    EntityType: nameof(Account),
                    EntityId: DeriveOpaqueEntityId(targetIdentifierHash),
                    BeforeState: null,
                    AfterState: new
                    {
                        TargetIdentifierHash = Convert.ToHexString(targetIdentifierHash),
                        ActorIpHash = actorIpHash,
                        CorrelationId = correlationId,
                    },
                    Reason: "customer.register.duplicate"),
                cancellationToken);

            return RegisterHandlerResult.Accepted();
        }

        var token = IdentityOpaqueTokenCodec.Create();
        var tokenSecretHash = tokenSecretHasher.HashSecret(token.Secret);

        var passwordHash = hasher.HashPassword(request.Password, SurfaceKind.Customer);

        var account = new Account
        {
            Id = Guid.NewGuid(),
            Surface = "customer",
            MarketCode = marketCode.Value,
            EmailNormalized = emailNormalized,
            EmailDisplay = email,
            PhoneE164 = normalizedPhone.E164,
            PhoneMarketCode = normalizedPhone.InferredMarketCode.Value,
            PasswordHash = passwordHash,
            PasswordHashVersion = 1,
            Status = "pending_email_verification",
            Locale = request.Locale.Trim().ToLowerInvariant(),
            DisplayName = request.DisplayName.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        dbContext.Accounts.Add(account);
        dbContext.EmailVerificationChallenges.Add(new EmailVerificationChallenge
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            TokenId = token.TokenId,
            TokenSecretHash = tokenSecretHash,
            TokenHash = tokenSecretHash,
            CreatedAt = now,
            ExpiresAt = now.AddHours(24),
            Status = "pending",
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await emailDispatcher.DispatchAsync(
                new IdentityEmailDispatchRequest(
                    MessageId: Guid.NewGuid(),
                    Surface: SurfaceKind.Customer,
                    Destination: account.EmailDisplay,
                    Purpose: "email_verification",
                    Token: token.ToString(),
                    CorrelationId: correlationId),
                cancellationToken);
        }
        catch (IdentityDeliveryNotConfiguredException)
        {
            return RegisterHandlerResult.Fail(
                StatusCodes.Status503ServiceUnavailable,
                "identity.email.dispatch_unavailable",
                "Email service unavailable",
                "Email delivery is temporarily unavailable.");
        }

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: account.Id,
                ActorRole: "customer",
                Action: "account.created",
                EntityType: nameof(Account),
                EntityId: account.Id,
                BeforeState: null,
                AfterState: new
                {
                    account.Id,
                    account.Surface,
                    account.MarketCode,
                    account.EmailDisplay,
                },
                Reason: "customer.register"),
            cancellationToken);

        return RegisterHandlerResult.Accepted();
    }

    private static Guid DeriveOpaqueEntityId(byte[] targetIdentifierHash)
    {
        return new Guid(targetIdentifierHash.AsSpan(0, 16));
    }
}

public sealed record RegisterHandlerResult(
    bool IsSuccess,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail)
{
    public static RegisterHandlerResult Accepted()
    {
        return new RegisterHandlerResult(
            IsSuccess: true,
            StatusCode: StatusCodes.Status202Accepted,
            ReasonCode: null,
            Title: null,
            Detail: null);
    }

    public static RegisterHandlerResult Fail(
        int statusCode,
        string reasonCode,
        string title,
        string detail)
    {
        return new RegisterHandlerResult(
            IsSuccess: false,
            StatusCode: statusCode,
            ReasonCode: reasonCode,
            Title: title,
            Detail: detail);
    }
}
