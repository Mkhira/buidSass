using BackendApi.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Notifications.Persistence.Configurations;

public sealed class UnsubscribeTokenConfiguration : IEntityTypeConfiguration<UnsubscribeToken>
{
    public void Configure(EntityTypeBuilder<UnsubscribeToken> builder)
    {
        builder.ToTable("unsubscribe_tokens", "notifications", t =>
        {
            t.HasCheckConstraint("CK_unsubscribe_tokens_channel",
                @"""Channel"" IN ('sms','email','push')");
            t.HasCheckConstraint("CK_unsubscribe_tokens_category_marketing",
                @"""Category"" = 'marketing'");
        });

        builder.HasKey(x => x.TokenHash);
        builder.Property(x => x.TokenHash).HasColumnType("bytea").IsRequired();
        builder.Property(x => x.CustomerId).IsRequired();
        builder.Property(x => x.Channel).HasColumnType("text").IsRequired();
        builder.Property(x => x.Category).HasColumnType("text").IsRequired();
        builder.Property(x => x.ExpiresAt).IsRequired();
        builder.Property(x => x.UsedAt);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.CustomerId)
            .HasDatabaseName("IX_unsubscribe_tokens_customer");

        builder.HasIndex(x => x.ExpiresAt)
            .HasDatabaseName("IX_unsubscribe_tokens_expires");
    }
}
