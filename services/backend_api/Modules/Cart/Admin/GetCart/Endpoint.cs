using BackendApi.Modules.Cart.Admin.Common;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.AuditLog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cart.Admin.GetCart;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminGetCartEndpoint(this IEndpointRouteBuilder builder)
    {
        var adminAuth = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };
        // Spec contract: GET /v1/admin/cart/carts/{cartId}
        builder.MapGet("/carts/{cartId:guid}", HandleAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission("cart.admin.read");
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid cartId,
        HttpContext context,
        CartDbContext db,
        CatalogDbContext catalogDb,
        CartViewBuilder viewBuilder,
        CustomerContextResolver customerContextResolver,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken ct)
    {
        var cart = await db.Carts.AsNoTracking().SingleOrDefaultAsync(c => c.Id == cartId, ct);
        if (cart is null)
        {
            return AdminCartResponseFactory.Problem(context, 404, "cart.not_found", "Cart not found", "");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var lines = await db.CartLines.AsNoTracking().Where(l => l.CartId == cart.Id).OrderBy(l => l.AddedAt).ToListAsync(ct);
        var saved = await db.CartSavedItems.AsNoTracking().Where(s => s.CartId == cart.Id).ToListAsync(ct);
        var b2b = await db.CartB2BMetadata.AsNoTracking().SingleOrDefaultAsync(b => b.CartId == cart.Id, ct);
        var ctxInfo = await customerContextResolver.ResolveAsync(cart.AccountId, ct);
        var view = await viewBuilder.BuildAsync(cart, lines, saved, b2b, catalogDb, ctxInfo.VerifiedForRestriction, ctxInfo.IsB2B, nowUtc, ct);

        // Principle 25: admin reads of customer carts are audited. Action name per data-model.md §84.
        var actorId = AdminCartResponseFactory.ResolveActorAccountId(context);
        if (actorId != Guid.Empty)
        {
            await auditEventPublisher.PublishAsync(new AuditEvent(
                actorId,
                "admin",
                "cart.admin_viewed",
                nameof(Entities.Cart),
                cart.Id,
                null,
                new { cart.Id, cart.AccountId, cart.MarketCode, cart.Status },
                "cart.admin.read"), ct);
        }

        return Results.Ok(view);
    }
}
