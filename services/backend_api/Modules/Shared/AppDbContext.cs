using BackendApi.Features.Seeding;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Storage;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Shared;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();
    public DbSet<SeedApplied> SeedApplied => Set<SeedApplied>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLogEntry>(entity =>
        {
            entity.ToTable("audit_log_entries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ActorRole).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(100).IsRequired();
            entity.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CorrelationId).IsRequired();
            entity.Property(x => x.OccurredAt).IsRequired();
            entity.HasIndex(x => new { x.EntityType, x.EntityId });
            entity.HasIndex(x => x.ActorId);
            entity.HasIndex(x => x.CorrelationId);
            entity.HasIndex(x => x.OccurredAt);
        });

        modelBuilder.Entity<StoredFile>(entity =>
        {
            entity.ToTable("stored_files");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.BucketKey).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Market).HasMaxLength(10).IsRequired();
            entity.Property(x => x.OriginalFilename).HasMaxLength(255);
            entity.Property(x => x.MimeType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.VirusScanStatus).HasMaxLength(20).IsRequired();
            entity.Property(x => x.UploadedAt).IsRequired();
        });

        modelBuilder.Entity<SeedApplied>(entity =>
        {
            entity.ToTable("seed_applied");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SeederName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.SeederVersion).IsRequired();
            entity.Property(x => x.Checksum).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Environment).HasMaxLength(32).IsRequired();
            entity.Property(x => x.AppliedAt).IsRequired();
            entity.HasIndex(x => new { x.SeederName, x.SeederVersion, x.Environment }).IsUnique();
        });
    }
}
