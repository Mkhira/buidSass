using System.Security.Cryptography;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Admin.Common;

public sealed class AdminPartialAuthTokenStore(IdentityDbContext dbContext)
{
    private readonly IdentityDbContext _dbContext = dbContext;

    public async Task<string> IssueAsync(Guid accountId, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        var tokenId = Guid.NewGuid();
        var tokenSecret = AdminIdentityResponseFactory.CreateOpaqueToken();

        _dbContext.AdminPartialAuthTokens.Add(new AdminPartialAuthToken
        {
            Id = tokenId,
            AccountId = accountId,
            TokenSecretHash = AdminIdentityResponseFactory.HashString(tokenSecret),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime),
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return $"{tokenId:N}.{tokenSecret}";
    }

    public async Task<PartialAuthSession?> TryGetAsync(string token, CancellationToken cancellationToken)
    {
        if (!TryParseToken(token, out var tokenId, out var tokenSecret))
        {
            return null;
        }

        var providedHash = AdminIdentityResponseFactory.HashString(tokenSecret);
        var stored = await _dbContext.AdminPartialAuthTokens.SingleOrDefaultAsync(
            x => x.Id == tokenId,
            cancellationToken);

        if (stored is null)
        {
            _ = CryptographicOperations.FixedTimeEquals(providedHash, new byte[providedHash.Length]);
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(stored.TokenSecretHash, providedHash))
        {
            return null;
        }

        if (stored.ConsumedAt is not null || stored.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        return new PartialAuthSession(stored.Id, stored.AccountId, stored.ExpiresAt);
    }

    public Task ConsumeAsync(Guid tokenId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return _dbContext.AdminPartialAuthTokens
            .Where(x => x.Id == tokenId && x.ConsumedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.ConsumedAt, _ => now),
                cancellationToken);
    }

    private static bool TryParseToken(string token, out Guid tokenId, out string tokenSecret)
    {
        tokenId = Guid.Empty;
        tokenSecret = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var separator = token.IndexOf('.');
        if (separator <= 0 || separator == token.Length - 1)
        {
            return false;
        }

        var tokenIdRaw = token[..separator];
        tokenSecret = token[(separator + 1)..];
        return Guid.TryParse(tokenIdRaw, out tokenId);
    }
}

public readonly record struct PartialAuthSession(Guid TokenId, Guid AccountId, DateTimeOffset ExpiresAt);

public sealed class AdminMfaChallengeStore(IdentityDbContext dbContext)
{
    private readonly IdentityDbContext _dbContext = dbContext;

    public async Task<Guid> IssueAsync(
        Guid accountId,
        Guid factorId,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        var challengeId = Guid.NewGuid();
        _dbContext.AdminMfaChallenges.Add(new AdminMfaChallenge
        {
            Id = challengeId,
            AccountId = accountId,
            FactorId = factorId,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime),
            Attempts = 0,
            MaxAttempts = 3,
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return challengeId;
    }

    public async Task<MfaChallengeLookupResult> TryGetAsync(Guid challengeId, CancellationToken cancellationToken)
    {
        var challenge = await _dbContext.AdminMfaChallenges.SingleOrDefaultAsync(
            x => x.Id == challengeId,
            cancellationToken);

        if (challenge is null)
        {
            return MfaChallengeLookupResult.Invalid();
        }

        if (!CryptographicOperations.FixedTimeEquals(challengeId.ToByteArray(), challenge.Id.ToByteArray()))
        {
            return MfaChallengeLookupResult.Invalid();
        }

        if (challenge.ConsumedAt is not null)
        {
            return MfaChallengeLookupResult.Consumed();
        }

        if (challenge.ExhaustedAt is not null || challenge.Attempts >= challenge.MaxAttempts)
        {
            return MfaChallengeLookupResult.Exhausted();
        }

        if (challenge.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return MfaChallengeLookupResult.Expired();
        }

        return MfaChallengeLookupResult.Active(
            new MfaChallengeState(
                challenge.Id,
                challenge.AccountId,
                challenge.FactorId,
                challenge.ExpiresAt,
                challenge.Attempts,
                challenge.MaxAttempts));
    }

    public async Task<MfaChallengeAttemptResult> RegisterFailedAttemptAsync(Guid challengeId, CancellationToken cancellationToken)
    {
        var challenge = await _dbContext.AdminMfaChallenges.SingleOrDefaultAsync(
            x => x.Id == challengeId,
            cancellationToken);

        if (challenge is null
            || challenge.ConsumedAt is not null
            || challenge.ExhaustedAt is not null
            || challenge.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return new MfaChallengeAttemptResult(0, true);
        }

        challenge.Attempts++;
        if (challenge.Attempts >= challenge.MaxAttempts)
        {
            challenge.ExhaustedAt = DateTimeOffset.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new MfaChallengeAttemptResult(challenge.Attempts, challenge.ExhaustedAt is not null);
    }

    public Task ConsumeAsync(Guid challengeId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return _dbContext.AdminMfaChallenges
            .Where(x => x.Id == challengeId && x.ConsumedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.ConsumedAt, _ => now),
                cancellationToken);
    }
}

public readonly record struct MfaChallengeState(
    Guid ChallengeId,
    Guid AccountId,
    Guid FactorId,
    DateTimeOffset ExpiresAt,
    short Attempts,
    short MaxAttempts);

public sealed record MfaChallengeLookupResult(
    MfaChallengeLookupStatus Status,
    MfaChallengeState? ChallengeState)
{
    public static MfaChallengeLookupResult Active(MfaChallengeState state) =>
        new(MfaChallengeLookupStatus.Active, state);

    public static MfaChallengeLookupResult Invalid() =>
        new(MfaChallengeLookupStatus.Invalid, null);

    public static MfaChallengeLookupResult Expired() =>
        new(MfaChallengeLookupStatus.Expired, null);

    public static MfaChallengeLookupResult Consumed() =>
        new(MfaChallengeLookupStatus.Consumed, null);

    public static MfaChallengeLookupResult Exhausted() =>
        new(MfaChallengeLookupStatus.Exhausted, null);
}

public enum MfaChallengeLookupStatus
{
    Active = 0,
    Invalid = 1,
    Expired = 2,
    Consumed = 3,
    Exhausted = 4,
}

public sealed record MfaChallengeAttemptResult(short Attempts, bool IsExhausted);
