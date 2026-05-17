using BackendApi.Modules.Payments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Payments.Persistence.Configurations;

public sealed class PciScopeEventConfiguration : IEntityTypeConfiguration<PciScopeEvent>
{
    public void Configure(EntityTypeBuilder<PciScopeEvent> builder)
    {
        builder.ToTable("pci_scope_events", "payments", t =>
        {
            t.HasCheckConstraint("CK_pci_scope_events_kind",
                @"""EventKind"" IN ('kv_slot_added','kv_slot_removed','hosted_fields_domain_changed','provider_added','provider_removed')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.EventKind).HasColumnType("text").IsRequired();
        builder.Property(x => x.ChangedBy).IsRequired();
        builder.Property(x => x.ChangeSummary).HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.CreatedAt).IsDescending().HasDatabaseName("IX_pci_scope_events_created_desc");
    }
}
