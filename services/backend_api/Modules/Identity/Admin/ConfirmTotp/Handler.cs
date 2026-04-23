using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.AspNetCore.DataProtection;
using OtpNet;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Admin.ConfirmTotp;

public static class ConfirmTotpHandler
{
    public static async Task<ConfirmTotpHandlerResult> HandleAsync(
        ConfirmTotpRequest request,
        IdentityDbContext dbContext,
        AdminPartialAuthTokenStore partialAuthStore,
        IDataProtectionProvider dataProtectionProvider,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var partialAuthSession = await partialAuthStore.TryGetAsync(request.PartialAuthToken, cancellationToken);
        if (partialAuthSession is null)
        {
            return ConfirmTotpHandlerResult.Fail(
                StatusCodes.Status401Unauthorized,
                "identity.partial_auth.invalid",
                "Invalid partial authentication",
                "The partial authentication token is invalid or expired.");
        }

        var factor = await dbContext.AdminMfaFactors.SingleOrDefaultAsync(
            x => x.Id == request.FactorId && x.AccountId == partialAuthSession.Value.AccountId && x.RevokedAt == null,
            cancellationToken);

        if (factor is null)
        {
            return ConfirmTotpHandlerResult.Fail(
                StatusCodes.Status404NotFound,
                "identity.mfa.factor_not_found",
                "MFA factor not found",
                "The requested MFA factor was not found.");
        }

        var protector = dataProtectionProvider.CreateProtector("identity.admin.totp.secret.v1");
        byte[] secretBytes;
        try
        {
            secretBytes = TotpSecretCodec.Decode(protector, factor.SecretEncrypted);
        }
        catch (TotpSecretUnprotectFailed)
        {
            return ConfirmTotpHandlerResult.Fail(
                StatusCodes.Status503ServiceUnavailable,
                "identity.mfa.secret_unprotect_failed",
                "MFA factor unavailable",
                "MFA confirmation is temporarily unavailable. Contact support.");
        }

        var totp = new Totp(secretBytes);
        var isValid = totp.VerifyTotp(
            request.Code,
            out _,
            VerificationWindow.RfcSpecifiedNetworkDelay);

        if (!isValid)
        {
            return ConfirmTotpHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.mfa.invalid_code",
                "Invalid TOTP code",
                "The provided TOTP code is invalid.");
        }

        factor.ConfirmedAt = DateTimeOffset.UtcNow;
        await partialAuthStore.ConsumeAsync(partialAuthSession.Value.TokenId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: partialAuthSession.Value.AccountId,
                ActorRole: "admin",
                Action: "admin.mfa.enrolment_confirmed",
                EntityType: "AdminMfaFactor",
                EntityId: factor.Id,
                BeforeState: new { ConfirmedAt = (DateTimeOffset?)null },
                AfterState: new { factor.ConfirmedAt },
                Reason: "mfa_enrolment"),
            cancellationToken);

        return ConfirmTotpHandlerResult.Success();
    }
}

public sealed record ConfirmTotpHandlerResult(
    bool IsSuccess,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail)
{
    public static ConfirmTotpHandlerResult Success()
    {
        return new ConfirmTotpHandlerResult(true, StatusCodes.Status200OK, null, null, null);
    }

    public static ConfirmTotpHandlerResult Fail(int statusCode, string reasonCode, string title, string detail)
    {
        return new ConfirmTotpHandlerResult(false, statusCode, reasonCode, title, detail);
    }
}
