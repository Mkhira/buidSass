using BackendApi.Modules.Cart.Primitives;
using FluentAssertions;
using Microsoft.Extensions.Hosting;

namespace Cart.Tests.Unit;

public sealed class CartOptionsValidatorTests
{
    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "BackendApi";
        public string ContentRootPath { get; set; } = "/tmp";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public void ProdDefaultSecret_IsRejected()
    {
        var env = new StubHostEnvironment { EnvironmentName = "Production" };
        var validator = new CartOptionsValidator(env);

        var result = validator.Validate(null, new CartOptions { TokenSecret = CartOptions.DefaultTokenSecret });

        result.Failed.Should().BeTrue();
        string.Join(";", result.Failures!).Should().Contain("well-known default");
    }

    [Fact]
    public void ProdShortSecret_IsRejected()
    {
        var env = new StubHostEnvironment { EnvironmentName = "Production" };
        var validator = new CartOptionsValidator(env);

        var result = validator.Validate(null, new CartOptions { TokenSecret = "too-short" });

        result.Failed.Should().BeTrue();
        string.Join(";", result.Failures!).Should().Contain("at least 32");
    }

    [Fact]
    public void ProdSecretOk_Passes()
    {
        var env = new StubHostEnvironment { EnvironmentName = "Production" };
        var validator = new CartOptionsValidator(env);

        var result = validator.Validate(null, new CartOptions
        {
            TokenSecret = new string('x', CartOptions.MinProductionTokenSecretLength),
        });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void DevDefaultSecret_Allowed()
    {
        var env = new StubHostEnvironment { EnvironmentName = "Development" };
        var validator = new CartOptionsValidator(env);

        var result = validator.Validate(null, new CartOptions { TokenSecret = CartOptions.DefaultTokenSecret });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void TestEnv_AllowsDefault()
    {
        var env = new StubHostEnvironment { EnvironmentName = "Test" };
        var validator = new CartOptionsValidator(env);

        var result = validator.Validate(null, new CartOptions { TokenSecret = CartOptions.DefaultTokenSecret });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void NonPositiveThresholds_Rejected()
    {
        var env = new StubHostEnvironment { EnvironmentName = "Development" };
        var validator = new CartOptionsValidator(env);

        var result = validator.Validate(null, new CartOptions
        {
            AbandonmentIdleMinutes = 0,
            AbandonmentDedupeHours = -1,
            GuestCartPurgeDays = 0,
            ArchivedCartRetentionDays = 0,
            MaxLinesPerCart = 0,
            TokenLifetimeDays = 0,
        });

        result.Failed.Should().BeTrue();
        var joined = string.Join(";", result.Failures!);
        joined.Should().Contain("AbandonmentIdleMinutes");
        joined.Should().Contain("AbandonmentDedupeHours");
        joined.Should().Contain("MaxLinesPerCart");
    }
}
