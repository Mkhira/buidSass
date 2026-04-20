using Azure.Identity;

namespace BackendApi.Configuration;

public static class ConfigurationExtensions
{
    public static WebApplicationBuilder AddLayeredConfiguration(this WebApplicationBuilder builder)
    {
        var env = builder.Environment.EnvironmentName;

        builder.Configuration
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        if (builder.Environment.IsStaging() || builder.Environment.IsProduction())
        {
            var vaultUri = builder.Configuration["KeyVault:Uri"]
                ?? throw new InvalidOperationException(
                    $"KeyVault:Uri missing for environment {env}. Set it in appsettings.{env}.json.");

            builder.Configuration.AddAzureKeyVault(
                new Uri(vaultUri),
                new DefaultAzureCredential());
        }

        return builder;
    }
}
