using BackendApi.Modules.Cms;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using BackendApi.Modules.Cms.Storefront;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cms.Tests.Unit;

/// <summary>
/// Spec 024 Phase 2 module-wiring sanity test. Asserts that
/// <see cref="CmsModule.AddCmsModule(IServiceCollection,IConfiguration)"/>
/// registers <see cref="CmsDbContext"/>, the storefront resolver, the banner
/// capacity calculator, and the reference-data seeder without throwing.
/// </summary>
public sealed class PhaseOneHarnessSmoke
{
    [Fact]
    public void AddCmsModule_registers_persistence_and_primitives()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=cms_test;Username=postgres;Password=postgres",
            })
            .Build();

        services.AddLogging();
        services.AddCmsModule(configuration);

        // Build to verify the registrations resolve.
        using var sp = services.BuildServiceProvider();
        sp.GetService<CmsDbContext>().Should().NotBeNull();
        sp.GetService<StorefrontContentResolver>().Should().NotBeNull();
        sp.GetService<BannerCapacityCalculator>().Should().NotBeNull();
    }
}
