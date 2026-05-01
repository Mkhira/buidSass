using BackendApi.Modules.Cms;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cms.Tests.Unit;

/// <summary>
/// Spec 024 Phase 1 — sanity test that the CMS module's empty registration
/// wires into a service collection without throwing. Replaced in Phase 2 by
/// the full CmsDbContext + slice registration tests.
/// </summary>
public sealed class PhaseOneHarnessSmoke
{
    [Fact]
    public void AddCmsModule_with_empty_registration_does_not_throw()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddCmsModule(configuration);

        services.Should().NotBeNull();
    }
}
