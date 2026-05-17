using BackendApi.Configuration;
using BackendApi.Modules.Notifications.Persistence;
using BackendApi.Modules.Notifications.Primitives;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace BackendApi.Modules.Notifications;

/// <summary>
/// DI + endpoint wiring for the Notifications vertical-slice module per
/// spec 025 (Phase 1E · Milestone 8). The module owns the 12-table
/// <c>notifications</c> schema, three state machines
/// (Notification / TemplateVersion / Campaign), and the 6-provider
/// abstraction (SES, SendGrid, Unifonic, Vodafone Egypt, Infobip, FCM).
///
/// Per the project-memory rule, <c>ManyServiceProvidersCreatedWarning</c>
/// is suppressed on both the DbContext and DbContextFactory registrations
/// so multi-WebApplicationFactory integration suites do not break the
/// Identity tests.
///
/// <para>
/// The task list (T001) references Hangfire queue config
/// (<c>otp-priority</c> + <c>default</c>). This repo standardized on
/// <see cref="Microsoft.Extensions.Hosting.BackgroundService"/> instead of
/// Hangfire (see spec 027 PaymentDispatchWorker for the precedent). BR-15
/// queue isolation is therefore implemented at the worker layer: dedicated
/// <c>OtpDispatchWorker</c> service for the OTP path, separate
/// <c>DispatchWorker</c> for everything else. Queue-name constants live
/// on <see cref="NotificationsConstants.Queues"/> so a future migration to
/// Hangfire is straightforward.
/// </para>
/// </summary>
public static partial class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var connectionString = configuration.ResolveRequiredDefaultConnectionString(hostEnvironment);

        services.AddDbContext<NotificationsDbContext>((provider, options) =>
        {
            var dataSource = provider.GetService<NpgsqlDataSource>();
            if (dataSource is not null) options.UseNpgsql(dataSource);
            else options.UseNpgsql(connectionString);
            // ManyServiceProvidersCreatedWarning suppressed (project-memory rule).
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        });

        services.AddDbContextFactory<NotificationsDbContext>((provider, options) =>
        {
            var dataSource = provider.GetService<NpgsqlDataSource>();
            if (dataSource is not null) options.UseNpgsql(dataSource);
            else options.UseNpgsql(connectionString);
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        }, lifetime: ServiceLifetime.Singleton);

        AddTemplating(services);
        AddProviders(services);
        AddSubscribers(services);
        AddWorkers(services, configuration);

        services.AddSingleton(TimeProvider.System);
        return services;
    }

    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var customer = endpoints.MapGroup("/notifications");
        MapCustomerEndpoints(customer);

        var admin = endpoints.MapGroup("/admin/notifications");
        MapAdminEndpoints(admin);

        var webhooks = endpoints.MapGroup("/notifications/webhooks");
        MapWebhookEndpoints(webhooks);

        return endpoints;
    }

    // Per-phase partials defined in later commits register concrete pieces.
    // Empty partials below allow Phase 0 to compile in isolation.
    static partial void AddTemplating(IServiceCollection services);
    static partial void AddProviders(IServiceCollection services);
    static partial void AddSubscribers(IServiceCollection services);
    static partial void AddWorkers(IServiceCollection services, IConfiguration configuration);
    static partial void MapCustomerEndpoints(IEndpointRouteBuilder customer);
    static partial void MapAdminEndpoints(IEndpointRouteBuilder admin);
    static partial void MapWebhookEndpoints(IEndpointRouteBuilder webhooks);
}
