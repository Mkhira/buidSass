using BackendApi.Modules.Payments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Payments.Persistence.Configurations;

public sealed class ReconciliationRunConfiguration : IEntityTypeConfiguration<ReconciliationRun>
{
    public void Configure(EntityTypeBuilder<ReconciliationRun> builder)
    {
        builder.ToTable("reconciliation_runs", "payments", t =>
        {
            t.HasCheckConstraint("CK_reconciliation_runs_status",
                @"""Status"" IN ('running','completed','failed')");
            t.HasCheckConstraint("CK_reconciliation_runs_date_range",
                "\"DateRangeStart\" <= \"DateRangeEnd\"");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.StartedAt).IsRequired();
        builder.Property(x => x.CompletedAt);
        builder.Property(x => x.DateRangeStart).IsRequired();
        builder.Property(x => x.DateRangeEnd).IsRequired();
        builder.Property(x => x.ProvidersProcessedJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.InternalPaymentsCount).IsRequired();
        builder.Property(x => x.ProviderLedgerRowsCount).IsRequired();
        builder.Property(x => x.MatchedCount).IsRequired();
        builder.Property(x => x.ExceptionsCount).IsRequired();
        builder.Property(x => x.Status).HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.StartedAt).IsDescending().HasDatabaseName("IX_reconciliation_runs_started_desc");
    }
}
