using BackendApi.Modules.Verification.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Verification.Persistence.Configurations;

/// <summary>EF configuration for <c>verification_reminders</c> per spec 020 data-model §2.5.</summary>
public sealed class VerificationReminderConfiguration : IEntityTypeConfiguration<VerificationReminder>
{
    public void Configure(EntityTypeBuilder<VerificationReminder> builder)
    {
        builder.ToTable("verification_reminders", "verification", t =>
        {
            t.HasCheckConstraint(
                "CK_verification_reminders_window_positive",
                "\"WindowDays\" > 0");
            t.HasCheckConstraint(
                "CK_verification_reminders_skip_reason_when_skipped",
                "\"Skipped\" = false OR (\"Skipped\" = true AND \"SkipReason\" IS NOT NULL)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.VerificationId).IsRequired();
        builder.Property(x => x.WindowDays).IsRequired();
        builder.Property(x => x.EmittedAt).IsRequired();
        builder.Property(x => x.Skipped).HasDefaultValue(false).IsRequired();
        builder.Property(x => x.SkipReason).HasColumnType("text");

        // §2.5 dedup invariant — UNIQUE (verification_id, window_days) — FR-019.
        builder.HasIndex(x => new { x.VerificationId, x.WindowDays })
            .IsUnique()
            .HasDatabaseName("UX_verification_reminders_verification_window");

        builder.HasOne<Entities.Verification>()
            .WithMany()
            .HasForeignKey(x => x.VerificationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
