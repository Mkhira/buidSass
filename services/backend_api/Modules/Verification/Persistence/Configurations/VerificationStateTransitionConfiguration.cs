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
            // CodeRabbit R2-3: pin PriorState/NewState to the state-machine wire
            // values at insert time. Append-only table — without DB-side guards a
            // single bad insert becomes a permanent invalid audit row. PriorState
            // additionally allows the '__none__' sentinel for the initial
            // submission insert (data-model §2.3).
            t.HasCheckConstraint(
                "CK_verification_state_transitions_prior_state_enum",
                "\"PriorState\" IN ('__none__','submitted','in-review','info-requested','approved','rejected','expired','revoked','superseded','void')");
            t.HasCheckConstraint(
                "CK_verification_state_transitions_new_state_enum",
                "\"NewState\" IN ('submitted','in-review','info-requested','approved','rejected','expired','revoked','superseded','void')");
            t.HasCheckConstraint(
                "CK_verification_state_transitions_market_code_enum",
                "\"MarketCode\" IN ('eg','ksa')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.VerificationId).IsRequired();
        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
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

        builder.HasIndex(x => new { x.MarketCode, x.OccurredAt })
            .HasDatabaseName("IX_verification_state_transitions_market_occurred");

        builder.HasOne<Entities.Verification>()
            .WithMany()
            .HasForeignKey(x => x.VerificationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
