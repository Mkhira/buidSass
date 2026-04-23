using System.Security.Cryptography;
using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OtpNet;

namespace BackendApi.Modules.Identity.Admin.EnrollTotp;

public static class EnrollTotpHandler
{
    public static async Task<EnrollTotpHandlerResult> HandleAsync(
        EnrollTotpRequest request,
        IdentityDbContext dbContext,
        AdminPartialAuthTokenStore partialAuthStore,
        Argon2idHasher hasher,
        IDataProtectionProvider dataProtectionProvider,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var partialAuthSession = await partialAuthStore.TryGetAsync(request.PartialAuthToken, cancellationToken);
        if (partialAuthSession is null)
        {
            return EnrollTotpHandlerResult.Fail(
                StatusCodes.Status401Unauthorized,
                "identity.partial_auth.invalid",
                "Invalid partial authentication",
                "The partial authentication token is invalid or expired.");
        }

        var account = await dbContext.Accounts.SingleOrDefaultAsync(
            x => x.Id == partialAuthSession.Value.AccountId && x.Surface == "admin",
            cancellationToken);

        if (account is null)
        {
            return EnrollTotpHandlerResult.Fail(
                StatusCodes.Status404NotFound,
                "identity.account.not_found",
                "Account not found",
                "The target admin account was not found.");
        }

        var existingFactors = await dbContext.AdminMfaFactors
            .Where(x => x.AccountId == account.Id && x.Kind == "totp" && x.RevokedAt == null)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        foreach (var existing in existingFactors)
        {
            existing.RevokedAt = now;
        }

        if (existingFactors.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var secretBytes = KeyGeneration.GenerateRandomKey(20);
        var secretBase32 = Base32Encoding.ToString(secretBytes);
        var otpauthUri = $"otpauth://totp/Dental%20Commerce%20Platform:{Uri.EscapeDataString(account.EmailDisplay)}?secret={secretBase32}&issuer=Dental%20Commerce%20Platform&algorithm=SHA1&digits=6&period=30";

        var recoveryCodes = Enumerable.Range(0, 10)
            .Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant())
            .ToArray();

        var protector = dataProtectionProvider.CreateProtector("identity.admin.totp.secret.v1");

        var factor = new AdminMfaFactor
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Kind = "totp",
            SecretEncrypted = TotpSecretCodec.Encode(protector, secretBytes),
            ConfirmedAt = null,
            CreatedAt = now,
            RecoveryCodesHash = JsonSerializer.Serialize(
                recoveryCodes.Select(code => new RecoveryCodeHashPayload(hasher.HashPassword(code, SurfaceKind.Admin), null))),
        };

        dbContext.AdminMfaFactors.Add(factor);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: account.Id,
                ActorRole: "admin",
                Action: "admin.mfa.enrolment_started",
                EntityType: nameof(AdminMfaFactor),
                EntityId: factor.Id,
                BeforeState: null,
                AfterState: new { factor.AccountId, factor.Kind, factor.CreatedAt },
                Reason: "mfa_enrolment"),
            cancellationToken);

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: account.Id,
                ActorRole: "admin",
                Action: "admin.mfa.recovery_code_issued",
                EntityType: nameof(AdminMfaFactor),
                EntityId: factor.Id,
                BeforeState: null,
                AfterState: new { Count = recoveryCodes.Length },
                Reason: "mfa_enrolment"),
            cancellationToken);

        return EnrollTotpHandlerResult.Success(factor.Id, otpauthUri, recoveryCodes);
    }
}

public sealed record EnrollTotpHandlerResult(
    bool IsSuccess,
    Guid FactorId,
    string? OtpauthUri,
    IReadOnlyList<string>? RecoveryCodes,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail)
{
    public static EnrollTotpHandlerResult Success(Guid factorId, string otpauthUri, IReadOnlyList<string> recoveryCodes)
    {
        return new EnrollTotpHandlerResult(
            true,
            factorId,
            otpauthUri,
            recoveryCodes,
            StatusCodes.Status200OK,
            null,
            null,
            null);
    }

    public static EnrollTotpHandlerResult Fail(
        int statusCode,
        string reasonCode,
        string title,
        string detail)
    {
        return new EnrollTotpHandlerResult(false, Guid.Empty, null, null, statusCode, reasonCode, title, detail);
    }
}

public sealed record RecoveryCodeHashPayload(string Hash, DateTimeOffset? UsedAt);
