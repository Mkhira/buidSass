using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Infrastructure;

public static class CustomerTestDataHelper
{
    public static async Task<CustomerSeedResult> SeedCustomerAsync(
        IdentityTestFactory factory,
        string email,
        string password,
        string phone = "+966501234567",
        bool emailVerified = true,
        bool phoneVerified = true)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<Argon2idHasher>();

        var now = DateTimeOffset.UtcNow;
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Surface = "customer",
            MarketCode = "ksa",
            EmailNormalized = email.Trim().ToLowerInvariant(),
            EmailDisplay = email,
            PhoneE164 = phone,
            PhoneMarketCode = "ksa",
            PasswordHash = hasher.HashPassword(password, SurfaceKind.Customer),
            PasswordHashVersion = 1,
            Status = "active",
            EmailVerifiedAt = emailVerified ? now : null,
            PhoneVerifiedAt = phoneVerified ? now : null,
            Locale = "ar",
            DisplayName = "Customer Test",
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        return new CustomerSeedResult(account.Id, account.EmailDisplay, password);
    }
}

public sealed record CustomerSeedResult(Guid AccountId, string Email, string Password);
