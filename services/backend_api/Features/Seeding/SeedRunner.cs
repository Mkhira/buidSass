using System.Security.Cryptography;
using System.Text;
using BackendApi.Features.Seeding.Datasets;
using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Features.Seeding;

/// <summary>
/// Orchestrates registered seeders: topological sort by DependsOn, SHA256 checksum over
/// (Name|Version|DatasetSize), idempotent writes to seed_applied. Re-run is a no-op for
/// seeders whose (Name, Version, Environment) row already exists.
/// </summary>
public sealed class SeedRunner(
    IEnumerable<ISeeder> seeders,
    AppDbContext db,
    IServiceProvider services,
    IHostEnvironment env,
    IConfiguration cfg,
    ILogger<SeedRunner> logger)
{
    public enum Mode { Apply, Fresh, DryRun }

    public async Task<int> RunAsync(Mode mode, CancellationToken ct)
    {
        SeedGuard.EnsureSafe(env, cfg);

        var options = new SeedingOptions
        {
            Enabled = cfg.GetValue<bool>($"{SeedingOptions.SectionName}:Enabled"),
            AutoApply = cfg.GetValue<bool>($"{SeedingOptions.SectionName}:AutoApply"),
            DatasetSize = cfg.GetValue<string>($"{SeedingOptions.SectionName}:DatasetSize") ?? "small",
        };

        if (!options.Enabled)
        {
            logger.LogWarning("Seeding disabled (Seeding:Enabled=false). Exiting no-op.");
            return 0;
        }

        var size = options.ParseDatasetSize();
        var ordered = TopologicalSort(seeders.ToList());

        if (mode == Mode.Fresh)
        {
            logger.LogWarning("Fresh mode: clearing seed_applied so all seeders re-run.");
            if (mode != Mode.DryRun)
            {
                await db.SeedApplied.ExecuteDeleteAsync(ct);
            }
        }

        var applied = 0;
        foreach (var seeder in ordered)
        {
            var checksum = Checksum(seeder, size);
            var existing = await db.SeedApplied
                .FirstOrDefaultAsync(x =>
                    x.SeederName == seeder.Name &&
                    x.SeederVersion == seeder.Version &&
                    x.Environment == env.EnvironmentName, ct);

            if (existing is not null && mode != Mode.Fresh)
            {
                logger.LogInformation("Skip {Name} v{Version}: already applied at {AppliedAt}.",
                    seeder.Name, seeder.Version, existing.AppliedAt);
                continue;
            }

            if (mode == Mode.DryRun)
            {
                logger.LogInformation("DryRun: would apply {Name} v{Version} (checksum {Checksum}).",
                    seeder.Name, seeder.Version, checksum);
                applied++;
                continue;
            }

            logger.LogInformation("Applying {Name} v{Version} (size {Size}).",
                seeder.Name, seeder.Version, size);

            var ctx = new SeedContext(db, services, size, env, logger);
            await seeder.ApplyAsync(ctx, ct);

            db.SeedApplied.Add(new SeedApplied
            {
                SeederName = seeder.Name,
                SeederVersion = seeder.Version,
                Checksum = checksum,
                Environment = env.EnvironmentName,
                AppliedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            applied++;
        }

        logger.LogInformation("Seeding complete: {Applied} seeder(s) applied.", applied);
        return applied;
    }

    private static string Checksum(ISeeder s, DatasetSize size)
    {
        var payload = $"{s.Name}|{s.Version}|{size}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static List<ISeeder> TopologicalSort(IReadOnlyList<ISeeder> input)
    {
        var byName = input.ToDictionary(s => s.Name, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ISeeder>(input.Count);

        void Visit(ISeeder s)
        {
            if (visited.Contains(s.Name)) return;
            if (!stack.Add(s.Name))
                throw new InvalidOperationException($"Seed dependency cycle detected at '{s.Name}'.");

            foreach (var dep in s.DependsOn)
            {
                if (!byName.TryGetValue(dep, out var depSeeder))
                    throw new InvalidOperationException(
                        $"Seeder '{s.Name}' depends on unregistered seeder '{dep}'.");
                Visit(depSeeder);
            }

            stack.Remove(s.Name);
            visited.Add(s.Name);
            result.Add(s);
        }

        foreach (var s in input.OrderBy(x => x.Name, StringComparer.Ordinal))
            Visit(s);

        return result;
    }
}
