using System.Text.Json;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Pricing.Admin.Common;
using BackendApi.Modules.Pricing.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Pricing.Admin.Explanations;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapExplanationEndpoints(this IEndpointRouteBuilder builder)
    {
        var adminAuth = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };
        builder.MapGet("/explanations/{ownerKind}/{ownerId:guid}", GetAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission("pricing.explanation.read");
        return builder;
    }

    private static async Task<IResult> GetAsync(
        string ownerKind,
        Guid ownerId,
        HttpContext context,
        PricingDbContext db,
        CancellationToken ct)
    {
        var kind = ownerKind.Trim().ToLowerInvariant();
        if (kind is not ("quote" or "order" or "preview"))
        {
            return AdminPricingResponseFactory.Problem(context, 400, "pricing.explanation.invalid_kind", "Invalid owner kind", "");
        }

        var row = await db.PriceExplanations
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.OwnerKind == kind && e.OwnerId == ownerId, ct);
        if (row is null)
        {
            return AdminPricingResponseFactory.Problem(context, 404, "pricing.explanation.not_found", "Not found", "");
        }

        var hashString = Convert.ToBase64String(row.ExplanationHash)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        using var doc = JsonDocument.Parse(row.ExplanationJson);
        return Results.Ok(new
        {
            id = row.Id,
            ownerKind = row.OwnerKind,
            ownerId = row.OwnerId,
            accountId = row.AccountId,
            marketCode = row.MarketCode,
            grandTotalMinor = row.GrandTotalMinor,
            createdAt = row.CreatedAt,
            explanation = doc.RootElement.Clone(),
            explanationHash = hashString,
        });
    }
}
