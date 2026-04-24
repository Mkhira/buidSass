using BackendApi.Modules.Checkout.Entities;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Checkout.Persistence;

public sealed class CheckoutDbContext(DbContextOptions<CheckoutDbContext> options) : DbContext(options)
{
    public DbSet<CheckoutSession> Sessions => Set<CheckoutSession>();
    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();
    public DbSet<ShippingQuote> ShippingQuotes => Set<ShippingQuote>();
    public DbSet<PaymentWebhookEvent> PaymentWebhookEvents => Set<PaymentWebhookEvent>();
    public DbSet<IdempotencyResult> IdempotencyResults => Set<IdempotencyResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("checkout");
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(CheckoutDbContext).Assembly,
            type => type.Namespace?.StartsWith("BackendApi.Modules.Checkout", StringComparison.Ordinal) == true);
    }
}
