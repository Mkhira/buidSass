using BackendApi.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Notifications.Persistence.Configurations;

public sealed class PreferenceConfiguration : IEntityTypeConfiguration<Preference>
{
    public void Configure(EntityTypeBuilder<Preference> builder)
    {
        builder.ToTable("preferences", "notifications", t =>
        {
            t.HasCheckConstraint("CK_preferences_channel",
                @"""Channel"" IN ('sms','email','push')");
            t.HasCheckConstraint("CK_preferences_category",
                @"""Category"" IN ('transactional','marketing')");
            // V-4 (defense-in-depth): transactional preferences cannot be turned off.
            t.HasCheckConstraint("CK_preferences_transactional_always_on",
                @"NOT (""Category"" = 'transactional' AND ""Enabled"" = false)");
        });

        builder.HasKey(x => new { x.CustomerId, x.Channel, x.Category });
        builder.Property(x => x.Enabled).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
    }
}
