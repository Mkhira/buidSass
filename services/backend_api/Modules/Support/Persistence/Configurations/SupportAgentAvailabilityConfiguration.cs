using BackendApi.Modules.Support.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Support.Persistence.Configurations;

public sealed class SupportAgentAvailabilityConfiguration : IEntityTypeConfiguration<SupportAgentAvailability>
{
    public void Configure(EntityTypeBuilder<SupportAgentAvailability> builder)
    {
        builder.ToTable("agent_availability", "support", t =>
        {
            t.HasCheckConstraint("CK_agent_availability_market_code", "\"MarketCode\" IN ('SA','EG')");
        });

        builder.HasKey(x => new { x.AgentId, x.MarketCode });
        builder.Property(x => x.AgentId).IsRequired();
        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.IsOnCall).IsRequired();
        builder.Property(x => x.LastToggledAtUtc).IsRequired();
        builder.Property(x => x.LastToggledByActorId);
    }
}
