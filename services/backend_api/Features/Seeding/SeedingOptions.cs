using BackendApi.Features.Seeding.Datasets;

namespace BackendApi.Features.Seeding;

public sealed class SeedingOptions
{
    public const string SectionName = "Seeding";

    public bool Enabled { get; init; } = false;
    public bool AutoApply { get; init; } = false;
    public string DatasetSize { get; init; } = "small";

    public DatasetSize ParseDatasetSize() => DatasetSize.Trim().ToLowerInvariant() switch
    {
        "small" => Datasets.DatasetSize.Small,
        "medium" => Datasets.DatasetSize.Medium,
        "large" => Datasets.DatasetSize.Large,
        _ => Datasets.DatasetSize.Small
    };
}
