using BackendApi.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Notifications.Persistence.Configurations;

public sealed class DeadLetterEntryConfiguration : IEntityTypeConfiguration<DeadLetterEntry>
{
    public void Configure(EntityTypeBuilder<DeadLetterEntry> builder)
    {
        builder.ToTable("dead_letter_queue", "notifications", t =>
        {
            t.HasCheckConstraint("CK_dead_letter_resolution",
                @"""Resolution"" IS NULL OR ""Resolution"" IN ('retry','discard')");
        });

        builder.HasKey(x => x.NotificationId);
        builder.Property(x => x.LastErrorMessageRedacted).HasColumnType("text");
        builder.Property(x => x.EnteredAt).IsRequired();
        builder.Property(x => x.ResolvedAt);
        builder.Property(x => x.Resolution).HasColumnType("text");
        builder.Property(x => x.ResolvedBy);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => x.EnteredAt)
            .HasDatabaseName("IX_dead_letter_entered_at");

        builder.HasIndex(x => x.ResolvedAt)
            .HasFilter("\"ResolvedAt\" IS NULL")
            .HasDatabaseName("IX_dead_letter_unresolved");
    }
}
