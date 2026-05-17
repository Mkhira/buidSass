using BackendApi.Modules.Payments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Payments.Persistence.Configurations;

public sealed class ReconciliationExceptionConfiguration : IEntityTypeConfiguration<ReconciliationException>
{
    public void Configure(EntityTypeBuilder<ReconciliationException> builder)
    {
        builder.ToTable("reconciliation_exceptions", "payments", t =>
        {
            t.HasCheckConstraint("CK_reconciliation_exceptions_reason",
                @"""Reason"" IN ('orphan_provider_row','missing_on_provider','amount_mismatch','currency_mismatch')");
            t.HasCheckConstraint("CK_reconciliation_exceptions_state",
                @"""State"" IN ('open','resolved')");
            t.HasCheckConstraint("CK_reconciliation_exceptions_resolution",
                @"""Resolution"" IS NULL OR ""Resolution"" IN ('refund_issued','internal_correction','provider_correction_requested','accepted_loss')");
            t.HasCheckConstraint("CK_reconciliation_exceptions_resolved_consistency",
                "(\"State\" = 'open' AND \"Resolution\" IS NULL AND \"ResolvedAt\" IS NULL AND \"ResolvedBy\" IS NULL) OR (\"State\" = 'resolved' AND \"Resolution\" IS NOT NULL AND \"ResolvedAt\" IS NOT NULL AND \"ResolvedBy\" IS NOT NULL)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.RunId).IsRequired();
        builder.Property(x => x.Reason).HasColumnType("text").IsRequired();
        builder.Property(x => x.ProviderId).HasColumnType("text");
        builder.Property(x => x.ProviderLedgerRowJson).HasColumnType("jsonb");
        builder.Property(x => x.InternalPaymentId);
        builder.Property(x => x.InternalAmount).HasColumnType("numeric(12,2)");
        builder.Property(x => x.ProviderAmount).HasColumnType("numeric(12,2)");
        builder.Property(x => x.State).HasColumnType("text").IsRequired();
        builder.Property(x => x.Resolution).HasColumnType("text");
        builder.Property(x => x.ResolvedBy);
        builder.Property(x => x.ResolutionNotes).HasColumnType("text");
        builder.Property(x => x.ResolvedAt);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.DeletedAt);

        builder.HasIndex(x => x.RunId).HasDatabaseName("IX_reconciliation_exceptions_run");
        builder.HasIndex(x => new { x.State, x.CreatedAt })
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_reconciliation_exceptions_state_created");
    }
}
