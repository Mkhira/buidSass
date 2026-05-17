using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackendApi.Modules.Payments.Persistence;

public sealed class PaymentsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PAYMENTS_DB_CONNECTION")
            ?? Environment.GetEnvironmentVariable("DEFAULT_DB_CONNECTION")
            ?? throw new InvalidOperationException(
                "Design-time EF operations require PAYMENTS_DB_CONNECTION or DEFAULT_DB_CONNECTION to be set.");

        var optionsBuilder = new DbContextOptionsBuilder<PaymentsDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new PaymentsDbContext(optionsBuilder.Options);
    }
}
