using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Features.Seeding;

public static class SeedingServiceCollectionExtensions
{
    public static IServiceCollection AddSeeding(this IServiceCollection services, IConfiguration cfg)
    {
        services.Configure<SeedingOptions>(cfg.GetSection(SeedingOptions.SectionName));
        services.AddScoped<SeedRunner>();
        return services;
    }
}
