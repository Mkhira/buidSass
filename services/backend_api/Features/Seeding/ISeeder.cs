using BackendApi.Features.Seeding.Datasets;
using BackendApi.Modules.Shared;
using Microsoft.Extensions.Logging;

namespace BackendApi.Features.Seeding;

public interface ISeeder
{
    string Name { get; }
    int Version { get; }
    IReadOnlyList<string> DependsOn { get; }
    Task ApplyAsync(SeedContext ctx, CancellationToken ct);
}

public sealed record SeedContext(
    AppDbContext Db,
    IServiceProvider Services,
    DatasetSize Size,
    IHostEnvironment Env,
    ILogger Logger);
