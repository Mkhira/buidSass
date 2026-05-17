using BackendApi.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Notifications.Persistence.Configurations;

public sealed class TemplateConfiguration : IEntityTypeConfiguration<Template>
{
    public void Configure(EntityTypeBuilder<Template> builder)
    {
        builder.ToTable("templates", "notifications");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.EventKind).HasColumnType("text").IsRequired();
        builder.Property(x => x.CurrentVersionId);
        builder.Property(x => x.State).HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.DeletedAt);

        builder.HasIndex(x => new { x.EventKind, x.DeletedAt })
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_templates_event_kind_active");

        // Soft-delete query filter.
        builder.HasQueryFilter(x => x.DeletedAt == null);
    }
}
