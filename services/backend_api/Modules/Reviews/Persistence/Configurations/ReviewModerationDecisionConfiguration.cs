using BackendApi.Modules.Reviews.Entities;
using BackendApi.Modules.Reviews.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BackendApi.Modules.Reviews.Persistence.Configurations;

/// <summary>
/// Append-only via Postgres trigger added in the init migration. Handlers
/// only ever INSERT into this table.
/// </summary>
public sealed class ReviewModerationDecisionConfiguration : IEntityTypeConfiguration<ReviewModerationDecision>
{
    public void Configure(EntityTypeBuilder<ReviewModerationDecision> builder)
    {
        builder.ToTable("review_moderation_decisions", "reviews");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.ReviewId).IsRequired();
        builder.Property(x => x.ActorId).IsRequired();
        builder.Property(x => x.ActorRole).HasColumnType("text").IsRequired();

        var stateConv = new ValueConverter<ReviewState, string>(
            v => ToWire(v),
            s => FromWire(s));
        builder.Property(x => x.FromState).HasConversion(stateConv).HasColumnType("text").IsRequired();
        builder.Property(x => x.ToState).HasConversion(stateConv).HasColumnType("text").IsRequired();

        builder.Property(x => x.TriggeredBy).HasColumnType("text").IsRequired();
        builder.Property(x => x.ReasonNote).HasColumnType("text");
        builder.Property(x => x.AdminNote).HasColumnType("text");
        builder.Property(x => x.BeforeJson).HasColumnType("jsonb").HasColumnName("BeforeJsonb");
        builder.Property(x => x.AfterJson).HasColumnType("jsonb").HasColumnName("AfterJsonb");
        builder.Property(x => x.CorrelationId);
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.ReviewId, x.CreatedAtUtc })
            .HasDatabaseName("IX_rmd_review_created");
        builder.HasIndex(x => new { x.ActorId, x.CreatedAtUtc })
            .HasDatabaseName("IX_rmd_actor_created");

        builder.HasOne<Review>()
            .WithMany()
            .HasForeignKey(x => x.ReviewId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static string ToWire(ReviewState state) => state switch
    {
        ReviewState.PendingModeration => "pending_moderation",
        ReviewState.Visible => "visible",
        ReviewState.Flagged => "flagged",
        ReviewState.Hidden => "hidden",
        ReviewState.Deleted => "deleted",
        _ => throw new InvalidOperationException($"Unknown ReviewState: {state}"),
    };

    private static ReviewState FromWire(string s) => s switch
    {
        "pending_moderation" => ReviewState.PendingModeration,
        "visible" => ReviewState.Visible,
        "flagged" => ReviewState.Flagged,
        "hidden" => ReviewState.Hidden,
        "deleted" => ReviewState.Deleted,
        _ => throw new InvalidOperationException($"Unknown ReviewState wire value: '{s}'."),
    };
}
