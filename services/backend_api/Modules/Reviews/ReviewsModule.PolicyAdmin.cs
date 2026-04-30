using BackendApi.Modules.Reviews.PolicyAdmin.DeleteWordlistTerm;
using BackendApi.Modules.Reviews.PolicyAdmin.ListWordlistTerms;
using BackendApi.Modules.Reviews.PolicyAdmin.UpdateMarketSchema;
using BackendApi.Modules.Reviews.PolicyAdmin.UpsertWordlistTerm;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Reviews;

/// <summary>
/// Companion partial implementations for the policy-admin surface (Phase J).
/// Wordlist CRUD + market-schema PATCH; wordlist mutations invalidate the
/// per-market in-process profanity-filter cache.
/// </summary>
public static partial class ReviewsModule
{
    static partial void AddPolicyAdminSlices(IServiceCollection services)
    {
        services.AddScoped<ListWordlistTermsHandler>();
        services.AddScoped<UpsertWordlistTermHandler>();
        services.AddScoped<DeleteWordlistTermHandler>();
        services.AddScoped<UpdateMarketSchemaHandler>();
    }

    static partial void MapPolicyAdminEndpoints(IEndpointRouteBuilder admin)
    {
        var wordlists = admin.MapGroup("/policy/wordlists");
        wordlists.MapListWordlistTermsEndpoint();
        wordlists.MapUpsertWordlistTermEndpoint();
        wordlists.MapDeleteWordlistTermEndpoint();

        var markets = admin.MapGroup("/policy/markets");
        markets.MapUpdateMarketSchemaEndpoint();
    }
}
