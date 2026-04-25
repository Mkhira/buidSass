using BackendApi.Modules.Checkout.Entities;
using BackendApi.Modules.Checkout.Persistence;
using BackendApi.Modules.Checkout.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Checkout.Customer.Common;

/// <summary>
/// Shared guard that every per-session slice runs: loads the session by id, asserts the
/// caller owns it (via account id OR matching cart token hash), checks expiry, and returns
/// either the entity or a pre-baked IResult that the slice can return directly.
/// </summary>
public static class CheckoutSessionLoader
{
    public sealed record LoadResult(CheckoutSession? Session, IResult? Problem);

    public static async Task<LoadResult> LoadAsync(
        CheckoutDbContext db,
        HttpContext context,
        Guid sessionId,
        Guid? accountId,
        string? suppliedCartToken,
        BackendApi.Modules.Cart.Primitives.CartTokenProvider cartTokenProvider,
        CancellationToken ct)
    {
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
        {
            return new LoadResult(null,
                CustomerCheckoutResponseFactory.Problem(context, 404, "checkout.session.not_found", "Session not found", ""));
        }

        // Ownership: prefer account match; fall back to token match for pre-auth sessions.
        if (session.AccountId is { } ownerId)
        {
            if (accountId != ownerId)
            {
                return new LoadResult(null,
                    CustomerCheckoutResponseFactory.Problem(context, 403, "checkout.session.not_owned", "Session not owned", ""));
            }
        }
        else if (session.CartTokenHash is { } hash)
        {
            // Guest session: token proof is REQUIRED before we trust the caller, even if they
            // arrive authenticated. Without this guard any logged-in user who learns a guest
            // sessionId could claim it just by hitting the loader (CR review on PR #30).
            if (string.IsNullOrWhiteSpace(suppliedCartToken)
                || !cartTokenProvider.TryDecode(suppliedCartToken, DateTimeOffset.UtcNow, out var suppliedHash)
                || !hash.AsSpan().SequenceEqual(suppliedHash))
            {
                return new LoadResult(null,
                    CustomerCheckoutResponseFactory.Problem(context, 403, "checkout.session.not_owned", "Session token mismatch", ""));
            }

            // Token verified — only now safe to bind the session to the authed caller.
            // CR review on PR #30 round 2: the claim is now atomic. Two concurrent callers
            // holding the same valid token race the conditional UPDATE — only the one that
            // matches `AccountId IS NULL AND CartTokenHash = @hash` wins; the loser falls
            // through to a fresh load and sees the bound session under the winner.
            if (accountId is not null)
            {
                var claimNow = DateTimeOffset.UtcNow;
                var rows = await db.Sessions
                    .Where(s => s.Id == session.Id && s.AccountId == null && s.CartTokenHash == hash)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(s => s.AccountId, accountId)
                        .SetProperty(s => s.CartTokenHash, (byte[]?)null)
                        .SetProperty(s => s.UpdatedAt, claimNow), ct);
                // Reload the row so the caller sees the durable state regardless of who won.
                session = await db.Sessions.SingleAsync(s => s.Id == session.Id, ct);
                if (rows == 0 && session.AccountId != accountId)
                {
                    // Lost the race AND the session ended up bound to someone else — refuse.
                    return new LoadResult(null,
                        CustomerCheckoutResponseFactory.Problem(context, 403, "checkout.session.not_owned",
                            "Session was claimed by another caller", ""));
                }
            }
        }

        var nowUtc = DateTimeOffset.UtcNow;
        if (session.State == CheckoutStates.Expired || session.ExpiresAt < nowUtc)
        {
            return new LoadResult(null,
                CustomerCheckoutResponseFactory.Problem(context, 410, "checkout.session.expired", "Session expired", ""));
        }
        if (session.State == CheckoutStates.Confirmed)
        {
            return new LoadResult(null,
                CustomerCheckoutResponseFactory.Problem(
                    context, 409, "checkout.already_submitted", "Session already confirmed",
                    "Session is in terminal state; start a new checkout.",
                    new Dictionary<string, object?> { ["orderId"] = session.OrderId }));
        }

        return new LoadResult(session, null);
    }
}
