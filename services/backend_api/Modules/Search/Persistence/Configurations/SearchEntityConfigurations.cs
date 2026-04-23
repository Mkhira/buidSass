using BackendApi.Modules.Search.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Search.Persistence.Configurations;

public sealed class SearchIndexerCursorConfiguration : IEntityTypeConfiguration<SearchIndexerCursor>
{
    public void Configure(EntityTypeBuilder<SearchIndexerCursor> builder)
    {
        builder.ToTable("search_indexer_cursor", "search");
        builder.HasKey(x => x.IndexName);
        builder.Property(x => x.IndexName).HasColumnType("citext").IsRequired();
        builder.Property(x => x.OutboxLastIdApplied).IsRequired();
        builder.Property(x => x.LastSuccessAt).IsRequired();
        builder.Property(x => x.LagSecondsLastObserved).HasDefaultValue(0).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.HasIndex(x => x.UpdatedAt);
    }
}

public sealed class ReindexJobConfiguration : IEntityTypeConfiguration<ReindexJob>
{
    public void Configure(EntityTypeBuilder<ReindexJob> builder)
    {
        builder.ToTable("reindex_jobs", "search");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.IndexName).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Status).HasColumnType("citext").IsRequired();
        builder.Property(x => x.StartedByAccountId).IsRequired();
        builder.Property(x => x.StartedAt).IsRequired();
        builder.Property(x => x.DocsWritten).HasDefaultValue(0).IsRequired();
        builder.HasIndex(x => new { x.IndexName, x.Status });
        builder.HasIndex(x => x.StartedAt);
        builder.HasIndex(x => x.IndexName)
            .IsUnique()
            .HasFilter("\"Status\" IN ('pending','running')");
    }
}
