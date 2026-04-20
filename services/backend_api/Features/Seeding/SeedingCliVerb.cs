using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Features.Seeding;

/// <summary>
/// Entrypoint for `dotnet run -- seed --mode=<apply|fresh|dry-run>`. Builds the host, resolves
/// <see cref="SeedRunner"/>, and exits with 0 on success, 1 on guard/validation failure, 2 on error.
/// </summary>
public static class SeedingCliVerb
{
    public const string Verb = "seed";

    public static async Task<int> RunAsync(WebApplication app, string[] args, CancellationToken ct)
    {
        var mode = ParseMode(args);
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("seed-cli");

        try
        {
            using var scope = app.Services.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<SeedRunner>();
            await runner.RunAsync(mode, ct);
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Seeding blocked: {Message}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Seeding failed with unhandled error.");
            return 2;
        }
    }

    private static SeedRunner.Mode ParseMode(string[] args)
    {
        foreach (var arg in args)
        {
            if (!arg.StartsWith("--mode=", StringComparison.Ordinal)) continue;
            var value = arg["--mode=".Length..].Trim().ToLowerInvariant();
            return value switch
            {
                "apply" => SeedRunner.Mode.Apply,
                "fresh" => SeedRunner.Mode.Fresh,
                "dry-run" or "dryrun" => SeedRunner.Mode.DryRun,
                _ => throw new ArgumentException(
                    $"Unknown --mode value '{value}'. Expected: apply | fresh | dry-run."),
            };
        }
        return SeedRunner.Mode.Apply;
    }
}
