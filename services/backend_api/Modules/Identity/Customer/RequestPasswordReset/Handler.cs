using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Customer.RequestPasswordReset;

public static class RequestPasswordResetHandler
{
    public static async Task<RequestPasswordResetHandlerResult> HandleAsync(
        RequestPasswordResetRequest request,
        string correlationId,
        IdentityDbContext dbContext,
        Argon2idHasher hasher,
        IdentityTokenSecretHasher tokenSecretHasher,
        IIdentityEmailDispatcher emailDispatcher,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var account = await dbContext.Accounts.SingleOrDefaultAsync(
            x => x.Surface == "customer" && x.EmailNormalized == normalizedEmail,
            cancellationToken);

        if (account is null)
        {
            _ = hasher.HashPassword(request.Email, SurfaceKind.Customer);
            _ = await dbContext.Accounts
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Select(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            return RequestPasswordResetHandlerResult.Success();
        }

        var rawToken = IdentityOpaqueTokenCodec.Create();
        var tokenSecretHash = tokenSecretHasher.HashSecret(rawToken.Secret);

        dbContext.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            TokenId = rawToken.TokenId,
            TokenSecretHash = tokenSecretHash,
            TokenHash = tokenSecretHash,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
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
                    Purpose: "password_reset",
                    Token: rawToken.ToString(),
                    CorrelationId: correlationId),
                cancellationToken);
        }
        catch (IdentityDeliveryNotConfiguredException)
        {
            return RequestPasswordResetHandlerResult.Fail(
                StatusCodes.Status503ServiceUnavailable,
                "identity.email.dispatch_unavailable",
                "Email service unavailable",
                "Email delivery is temporarily unavailable.");
        }

        return RequestPasswordResetHandlerResult.Success();
    }
}

public sealed record RequestPasswordResetHandlerResult(
    bool IsSuccess,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail)
{
    public static RequestPasswordResetHandlerResult Success() =>
        new(true, StatusCodes.Status202Accepted, null, null, null);

    public static RequestPasswordResetHandlerResult Fail(int statusCode, string reasonCode, string title, string detail) =>
        new(false, statusCode, reasonCode, title, detail);
}
