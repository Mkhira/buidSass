using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BackendApi.Features.Seeding;

/// <summary>
/// Precondition every seed-path call passes through. Hard-blocks seeding under Production
/// regardless of any config flag (belt + braces pattern defined in the A1 retrofit plan).
/// </summary>
public static class SeedGuard
{
    public const string ProductionBlockedMessage =
        "SeedGuard: seeding is hard-blocked in Production, regardless of config flags.";

    public static void EnsureSafe(IHostEnvironment env, IConfiguration cfg)
    {
        if (env.IsProduction())
            throw new InvalidOperationException(ProductionBlockedMessage);

        if (env.IsStaging())
        {
            var autoApply = cfg.GetValue<bool>($"{SeedingOptions.SectionName}:AutoApply");
            if (!autoApply)
                throw new InvalidOperationException(
                    "SeedGuard: Staging seeding requires Seeding:AutoApply=true.");
        }
    }
}
