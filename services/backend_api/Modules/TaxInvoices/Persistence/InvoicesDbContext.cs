using BackendApi.Modules.TaxInvoices.Entities;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.TaxInvoices.Persistence;

public sealed class InvoicesDbContext(DbContextOptions<InvoicesDbContext> options) : DbContext(options)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
    public DbSet<CreditNoteLine> CreditNoteLines => Set<CreditNoteLine>();
    public DbSet<InvoiceRenderJob> RenderJobs => Set<InvoiceRenderJob>();
    public DbSet<InvoiceTemplate> InvoiceTemplates => Set<InvoiceTemplate>();
    public DbSet<InvoicesOutboxEntry> Outbox => Set<InvoicesOutboxEntry>();
    public DbSet<SubscriptionCheckpoint> SubscriptionCheckpoints => Set<SubscriptionCheckpoint>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("invoices");
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(InvoicesDbContext).Assembly,
            type => type.Namespace?.StartsWith("BackendApi.Modules.TaxInvoices", StringComparison.Ordinal) == true);
    }
}
