using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Checkout.Customer.Common;
using BackendApi.Modules.Checkout.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Checkout.Customer.AcceptDrift;

public sealed record AcceptDriftRequest(long AcceptedTotalMinor, string? NewExplanationHash);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAcceptDriftEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/sessions/{sessionId:guid}/accept-drift", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid sessionId,
        AcceptDriftRequest request,
        HttpContext context,
        CheckoutDbContext db,
        CartTokenProvider cartTokenProvider,
        CancellationToken ct)
    {
        var accountId = CustomerCheckoutResponseFactory.ResolveAccountId(context);
        if (accountId is null)
        {
            return CustomerCheckoutResponseFactory.Problem(context, 401, "checkout.requires_auth", "Auth required", "");
        }
        var load = await CheckoutSessionLoader.LoadAsync(db, context, sessionId, accountId, suppliedCartToken: null, cartTokenProvider, ct);
        if (load.Problem is not null) return load.Problem;
        var session = load.Session!;

        // Record the acknowledgment — Submit re-checks the drift hash with AcceptedDriftAt
        // non-null + AcceptedTotalMinor matching the new grand total to clear the gate.
        session.AcceptedDriftAt = DateTimeOffset.UtcNow;
        session.LastTouchedAt = session.AcceptedDriftAt.Value;
        session.UpdatedAt = session.AcceptedDriftAt.Value;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (CustomerCheckoutResponseFactory.IsConcurrencyConflict(ex))
        {
            return CustomerCheckoutResponseFactory.Problem(context, 409, "checkout.concurrency_conflict", "Concurrency conflict", "");
        }
        return Results.Ok(new { sessionId = session.Id, state = session.State, acceptedDriftAt = session.AcceptedDriftAt });
    }
}
