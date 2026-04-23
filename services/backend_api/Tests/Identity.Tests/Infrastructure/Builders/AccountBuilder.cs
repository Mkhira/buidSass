using BackendApi.Modules.Identity.Entities;

namespace Identity.Tests.Infrastructure.Builders;

public sealed class AccountBuilder
{
    private readonly Account _account = new()
    {
        Id = Guid.NewGuid(),
        Surface = "customer",
        MarketCode = "ksa",
        EmailNormalized = "customer@local.dev",
        EmailDisplay = "customer@local.dev",
        PasswordHash = "$argon2id$dummy",
        PasswordHashVersion = 1,
        Status = "active",
        Locale = "ar",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    public AccountBuilder WithSurface(string surface)
    {
        _account.Surface = surface;
        return this;
    }

    public AccountBuilder WithMarket(string marketCode)
    {
        _account.MarketCode = marketCode;
        return this;
    }

    public AccountBuilder WithEmail(string email)
    {
        _account.EmailDisplay = email;
        _account.EmailNormalized = email.Trim().ToLowerInvariant();
        return this;
    }

    public Account Build() => _account;
}
