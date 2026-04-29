using BackendApi.Modules.Verification.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Verification.Persistence.Configurations;

/// <summary>
/// EF configuration for <c>verification_state_transitions</c> per spec 020
/// data-model §2.3. Append-only ledger; UPDATE/DELETE blocked by a Postgres
/// trigger added in the EF migration (T032).
/// </summary>
public sealed class VerificationStateTransitionConfiguration : IEntityTypeConfiguration<VerificationStateTransition>
{
    public void Configure(EntityTypeBuilder<VerificationStateTransition> builder)
    {
        builder.ToTable("verification_state_transitions", "verification", t =>
        {
            t.HasCheckConstraint(
                "CK_verification_state_transitions_actor_kind_enum",
                "\"ActorKind\" IN ('customer','reviewer','system')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.VerificationId).IsRequired();
        builder.Property(x => x.PriorState).HasColumnType("text").IsRequired();
        builder.Property(x => x.NewState).HasColumnType("text").IsRequired();
        builder.Property(x => x.ActorKind).HasColumnType("text").IsRequired();
        builder.Property(x => x.Reason).HasColumnType("text").IsRequired();
        builder.Property(x => x.MetadataJson)
            .HasColumnType("jsonb")
            .HasColumnName("Metadata")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();
        builder.Property(x => x.OccurredAt).IsRequired();

        builder.HasIndex(x => new { x.VerificationId, x.OccurredAt })
            .HasDatabaseName("IX_verification_state_transitions_verification_occurred");

        builder.HasOne<Entities.Verification>()
            .WithMany()
            .HasForeignKey(x => x.VerificationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
