using BackendApi.Modules.Payments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Payments.Persistence.Configurations;

public sealed class BankTransferReferenceConfiguration : IEntityTypeConfiguration<BankTransferReference>
{
    public void Configure(EntityTypeBuilder<BankTransferReference> builder)
    {
        builder.ToTable("bank_transfer_references", "payments", t =>
        {
            t.HasCheckConstraint("CK_bank_transfer_reference_format",
                "\"Reference\" ~ '^(SA|EG)-[0-9a-f]{8}-[A-Z0-9]{4}$'");
        });

        builder.HasKey(x => x.PaymentId);
        builder.Property(x => x.PaymentId);
        builder.Property(x => x.Reference).HasColumnType("text").IsRequired();
        builder.Property(x => x.MatchedBankStatementEntryJson).HasColumnType("jsonb");
        builder.Property(x => x.MatchedAt);
        builder.Property(x => x.MatchedBy);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.DeletedAt);

        builder.HasIndex(x => x.Reference)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("UX_bank_transfer_reference");
    }
}
