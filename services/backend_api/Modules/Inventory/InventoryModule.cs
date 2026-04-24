using BackendApi.Configuration;
using BackendApi.Features.Seeding;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Inventory.Primitives;
using BackendApi.Modules.Inventory.Primitives.Fefo;
using BackendApi.Modules.Inventory.Seeding;
using BackendApi.Modules.Inventory.Workers;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace BackendApi.Modules.Inventory;

public static class InventoryModule
{
    public static IServiceCollection AddInventoryModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var connectionString = configuration.ResolveRequiredDefaultConnectionString(hostEnvironment);

        services.AddDbContext<InventoryDbContext>((provider, options) =>
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
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        });

        services.Configure<InventoryOptions>(configuration.GetSection(InventoryOptions.SectionName));
        services.AddSingleton<AtsCalculator>();
        services.AddSingleton<BucketMapper>();
        services.AddSingleton<FefoPicker>();
        services.AddSingleton<ReorderAlertEmitter>();
        services.AddSingleton<AvailabilityEventEmitter>();
        services.AddHostedService<ReservationReleaseWorker>();
        services.AddHostedService<ExpiryWriteoffWorker>();

        services.AddScoped<ISeeder, InventoryBootstrapSeeder>();

        return services;
    }

    public static WebApplication UseInventoryModuleEndpoints(this WebApplication app)
    {
        var internalInventory = app.MapGroup("/v1/internal/inventory");
        Internal.Reservations.Create.Endpoint.MapCreateReservationEndpoint(internalInventory);
        Internal.Reservations.Release.Endpoint.MapReleaseReservationEndpoint(internalInventory);
        Internal.Reservations.Convert.Endpoint.MapConvertReservationEndpoint(internalInventory);
        Internal.Movements.Return.Endpoint.MapReturnMovementEndpoint(internalInventory);

        var adminInventory = app.MapGroup("/v1/admin/inventory");
        Admin.Batches.Endpoint.MapBatchEndpoints(adminInventory);
        Admin.Movements.Endpoint.MapMovementEndpoints(adminInventory);

        var customerInventory = app.MapGroup("/v1/customer/inventory");
        Customer.GetAvailability.Endpoint.MapGetAvailabilityEndpoint(customerInventory);

        return app;
    }
}
