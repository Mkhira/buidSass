using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackendApi.Modules.TaxInvoices.Persistence;

public sealed class InvoicesDbContextDesignTimeFactory : IDesignTimeDbContextFactory<InvoicesDbContext>
{
    public InvoicesDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("INVOICES_DB_CONNECTION")
            ?? Environment.GetEnvironmentVariable("DEFAULT_DB_CONNECTION")
            ?? throw new InvalidOperationException(
                "Design-time EF operations require INVOICES_DB_CONNECTION or DEFAULT_DB_CONNECTION to be set.");
        var options = new DbContextOptionsBuilder<InvoicesDbContext>().UseNpgsql(connectionString).Options;
        return new InvoicesDbContext(options);
    }
}
