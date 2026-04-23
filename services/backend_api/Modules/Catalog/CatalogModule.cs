using BackendApi.Configuration;
using BackendApi.Features.Seeding;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Catalog.Primitives;
using BackendApi.Modules.Catalog.Primitives.Outbox;
using BackendApi.Modules.Catalog.Primitives.Restriction;
using BackendApi.Modules.Catalog.Seeding;
using BackendApi.Modules.Catalog.Workers;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace BackendApi.Modules.Catalog;

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var connectionString = configuration.ResolveRequiredDefaultConnectionString(hostEnvironment);

        services.AddScoped<CatalogSaveChangesInterceptor>();
        services.AddDbContext<CatalogDbContext>((provider, options) =>
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
            options.AddInterceptors(provider.GetRequiredService<CatalogSaveChangesInterceptor>());
        });

        services.AddSingleton<CategoryTreeService>();
        services.AddSingleton<AttributeSchemaValidator>();
        services.AddSingleton<IImageVariantGenerator, ImageSharpVariantGenerator>();
        services.AddSingleton<ContentAddressedPaths>();
        services.AddSingleton<RestrictionCache>();
        services.AddScoped<RestrictionEvaluator>();
        services.AddScoped<CatalogOutboxWriter>();
        services.AddScoped<ICatalogEventSubscriber, LoggingCatalogEventSubscriber>();

        services.AddScoped<ISeeder, CategoryAttributeSchemaSeeder>();
        services.AddScoped<ISeeder, CatalogDevDataSeeder>();

        services.AddHostedService<MediaVariantWorker>();
        services.AddHostedService<ScheduledPublishWorker>();
        services.AddHostedService<CatalogOutboxDispatcherWorker>();

        return services;
    }

    public static WebApplication UseCatalogModuleEndpoints(this WebApplication app)
    {
        var customerCatalog = app.MapGroup("/v1/customer/catalog");
        Customer.ListCategories.ListCategoriesEndpoint.Map(customerCatalog);
        Customer.ListBrands.ListBrandsEndpoint.Map(customerCatalog);
        Customer.GetCategoryProducts.GetCategoryProductsEndpoint.Map(customerCatalog);
        Customer.GetProductBySlug.GetProductBySlugEndpoint.Map(customerCatalog);

        var internalCatalog = app.MapGroup("/v1/internal/catalog");
        Customer.CheckRestriction.CheckRestrictionEndpoint.Map(internalCatalog);

        var adminCatalog = app.MapGroup("/v1/admin/catalog");
        Admin.Categories.CategoryAdminEndpoints.Map(adminCatalog);
        Admin.Brands.BrandAdminEndpoints.Map(adminCatalog);
        Admin.Manufacturers.ManufacturerAdminEndpoints.Map(adminCatalog);
        Admin.Products.ProductAdminEndpoints.Map(adminCatalog);
        Admin.Media.MediaAdminEndpoints.Map(adminCatalog);
        Admin.Documents.DocumentAdminEndpoints.Map(adminCatalog);
        Admin.BulkImportProducts.BulkImportEndpoint.Map(adminCatalog);

        return app;
    }
}
