using BackendApi.Modules.Search.Admin.Health;
using BackendApi.Modules.Search.Admin.ListJobs;
using BackendApi.Modules.Search.Admin.Reindex;
using BackendApi.Configuration;
using BackendApi.Modules.Search.Customer.Autocomplete;
using BackendApi.Modules.Search.Customer.LookupBySkuOrBarcode;
using BackendApi.Modules.Search.Customer.SearchProducts;
using BackendApi.Modules.Search.Workers;
using BackendApi.Modules.Catalog.Primitives.Outbox;
using BackendApi.Modules.Search.Primitives.Normalization;
using BackendApi.Modules.Search.Primitives;
using BackendApi.Modules.Search.Persistence;
using BackendApi.Modules.Search.Synonyms;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace BackendApi.Modules.Search;

public static class SearchModule
{
    public static IServiceCollection AddSearchModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var connectionString = configuration.ResolveRequiredDefaultConnectionString(hostEnvironment);

        services.AddDbContext<SearchDbContext>((provider, options) =>
        {
            var dataSource = provider.GetService<NpgsqlDataSource>();
            if (dataSource is not null)
            {
                options.UseNpgsql(dataSource);
            }
            else
            {
                options.UseNpgsql(connectionString);
            }
        });

        services.Configure<MeilisearchOptions>(configuration.GetSection(MeilisearchOptions.SectionName));
        services.AddHttpClient<MeilisearchSearchEngine>();
        services.AddScoped<ISearchEngine, MeilisearchSearchEngine>();
        services.AddSingleton<SynonymsSeeder>();
        services.AddSingleton<QueryLogger>();
        services.AddSingleton<ArabicNormalizer>();
        services.AddSingleton<SearchReindexService>();
        services.AddSingleton<SearchIndexerWorker>();
        services.AddScoped<ICatalogEventSubscriber>(sp => sp.GetRequiredService<SearchIndexerWorker>());
        services.AddHostedService<SearchBootstrapHostedService>();

        return services;
    }

    public static WebApplication UseSearchModuleEndpoints(this WebApplication app)
    {
        var customerSearch = app.MapGroup("/v1/customer/search");
        customerSearch.MapSearchProductsEndpoint();
        customerSearch.MapLookupEndpoint();
        customerSearch.MapAutocompleteEndpoint();

        var adminSearch = app.MapGroup("/v1/admin/search");
        adminSearch.MapReindexEndpoints();
        adminSearch.MapHealthEndpoint();
        adminSearch.MapListJobsEndpoint();

        return app;
    }
}
