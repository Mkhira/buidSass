using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Shared;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Cart.Primitives;

/// <summary>
/// Wires customer sign-in → CartMerger (FR-003 / plan Phase F). The Identity SignIn endpoint
/// enumerates registered <see cref="ICustomerPostSignInHook"/> implementations; this one runs
/// the merge in-process using the authenticated account + any cart_token the client passed in.
/// </summary>
public sealed class CartLoginMergeHook(
    CartDbContext cartDb,
    CatalogDbContext catalogDb,
    InventoryDbContext inventoryDb,
    CartResolver resolver,
    CartMerger merger,
    CartViewBuilder viewBuilder,
    CartInventoryOrchestrator inventoryOrchestrator,
    CustomerContextResolver customerContextResolver,
    ILogger<CartLoginMergeHook> logger) : ICustomerPostSignInHook
{
    public async Task OnSignedInAsync(CustomerPostSignInContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.CartToken))
        {
            // No anon cart to merge. Nothing to do.
            return;
        }

        try
        {
            var outcome = await Customer.Merge.Endpoint.ExecuteAsync(
                context.AccountId,
                context.MarketCode,
                context.CartToken,
                cartDb,
                catalogDb,
                inventoryDb,
                resolver,
                merger,
                viewBuilder,
                inventoryOrchestrator,
                customerContextResolver,
                logger,
                DateTimeOffset.UtcNow,
                cancellationToken);

            if (outcome.ConflictDetail is not null)
            {
                logger.LogInformation(
                    "cart.login_merge.conflict accountId={AccountId} detail={Detail}",
                    context.AccountId, outcome.ConflictDetail);
            }
        }
        catch (Exception ex)
        {
            // Hooks MUST NOT abort sign-in; log and swallow (Identity endpoint also guards).
            logger.LogWarning(ex, "cart.login_merge.failed accountId={AccountId}", context.AccountId);
        }
    }
}
