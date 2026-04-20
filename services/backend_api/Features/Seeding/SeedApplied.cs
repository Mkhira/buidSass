namespace BackendApi.Features.Seeding;

public class SeedApplied
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SeederName { get; set; } = string.Empty;
    public int SeederVersion { get; set; }
    public string Checksum { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public DateTimeOffset AppliedAt { get; set; } = DateTimeOffset.UtcNow;
}
