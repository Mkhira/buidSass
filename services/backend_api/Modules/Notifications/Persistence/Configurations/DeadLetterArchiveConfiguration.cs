using BackendApi.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Notifications.Persistence.Configurations;

public sealed class DeadLetterArchiveConfiguration : IEntityTypeConfiguration<DeadLetterArchive>
{
    public void Configure(EntityTypeBuilder<DeadLetterArchive> builder)
    {
        builder.ToTable("dead_letter_queue_archive", "notifications");

        builder.HasKey(x => x.NotificationId);
        builder.Property(x => x.LastErrorMessageRedacted).HasColumnType("text");
        builder.Property(x => x.EnteredAt).IsRequired();
        builder.Property(x => x.ResolvedAt);
        builder.Property(x => x.Resolution).HasColumnType("text");
        builder.Property(x => x.ResolvedBy);
        builder.Property(x => x.ArchivedAt).IsRequired();

        builder.HasIndex(x => x.ArchivedAt)
            .HasDatabaseName("IX_dead_letter_archive_archived_at");
    }
}
