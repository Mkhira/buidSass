using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

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

    public static string ResolveRequiredDefaultConnectionString(
        this IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        if (hostEnvironment.IsEnvironment("Test"))
        {
            return "Host=localhost;Port=5432;Database=dental_commerce_test;Username=dental_api_app;Password=dental_api_app";
        }

        throw new InvalidOperationException(
            $"ConnectionStrings:DefaultConnection is required for environment '{hostEnvironment.EnvironmentName}'.");
    }
}
