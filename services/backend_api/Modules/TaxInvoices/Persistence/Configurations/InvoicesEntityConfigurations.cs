using BackendApi.Modules.TaxInvoices.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.TaxInvoices.Persistence.Configurations;

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices", "invoices", t =>
        {
            t.HasCheckConstraint("CK_invoices_invoices_state_enum",
                "\"State\" IN ('pending','rendered','delivered','failed')");
            t.HasCheckConstraint("CK_invoices_invoices_grand_total_non_negative",
                "\"GrandTotalMinor\" >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.InvoiceNumber).HasColumnType("text").IsRequired();
        builder.HasIndex(x => x.InvoiceNumber).IsUnique();
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Currency).HasColumnType("citext").IsRequired();
        builder.Property(x => x.State).HasColumnType("citext").IsRequired().HasDefaultValue(Invoice.StatePending);
        builder.Property(x => x.BillToJson).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.SellerJson).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasMany(x => x.Lines).WithOne(l => l.Invoice!).HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.CreditNotes).WithOne(c => c.Invoice!).HasForeignKey(c => c.InvoiceId).OnDelete(DeleteBehavior.Restrict);

        // CR3 fix — invoices are 1-per-order; enforce at the DB level so the `23505` retry
        // path in IssueOnCaptureHandler actually catches concurrent issuance races.
        builder.HasIndex(x => x.OrderId).IsUnique().HasDatabaseName("IX_invoices_invoices_order");
        builder.HasIndex(x => new { x.MarketCode, x.IssuedAt }).HasDatabaseName("IX_invoices_invoices_market_issued");
        builder.HasIndex(x => new { x.AccountId, x.IssuedAt }).HasDatabaseName("IX_invoices_invoices_account_issued");
        builder.HasIndex(x => x.State).HasDatabaseName("IX_invoices_invoices_state");
    }
}

public sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("invoice_lines", "invoices", t =>
        {
            t.HasCheckConstraint("CK_invoices_invoice_lines_qty_positive", "\"Qty\" > 0");
            t.HasCheckConstraint("CK_invoices_invoice_lines_tax_rate_bp_bounds",
                "\"TaxRateBp\" >= 0 AND \"TaxRateBp\" <= 10000");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Sku).HasColumnType("citext").IsRequired();
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.HasIndex(x => x.InvoiceId);
        builder.HasIndex(x => new { x.MarketCode, x.InvoiceId }).HasDatabaseName("IX_invoices_invoice_lines_market_invoice");
    }
}

public sealed class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> builder)
    {
        builder.ToTable("credit_notes", "invoices", t =>
        {
            t.HasCheckConstraint("CK_invoices_credit_notes_state_enum",
                "\"State\" IN ('pending','rendered','delivered','failed')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CreditNoteNumber).HasColumnType("text").IsRequired();
        builder.HasIndex(x => x.CreditNoteNumber).IsUnique();
        builder.Property(x => x.ReasonCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.State).HasColumnType("citext").IsRequired().HasDefaultValue(CreditNote.StatePending);
        builder.HasMany(x => x.Lines).WithOne(l => l.CreditNote!).HasForeignKey(l => l.CreditNoteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.InvoiceId);
        builder.HasIndex(x => new { x.MarketCode, x.IssuedAt }).HasDatabaseName("IX_invoices_credit_notes_market_issued");
        // Idempotency index: only one credit note per refund.
        builder.HasIndex(x => x.RefundId)
            .IsUnique()
            .HasFilter("\"RefundId\" IS NOT NULL")
            .HasDatabaseName("IX_invoices_credit_notes_refund_unique");
    }
}

public sealed class CreditNoteLineConfiguration : IEntityTypeConfiguration<CreditNoteLine>
{
    public void Configure(EntityTypeBuilder<CreditNoteLine> builder)
    {
        builder.ToTable("credit_note_lines", "invoices", t =>
        {
            t.HasCheckConstraint("CK_invoices_credit_note_lines_qty_positive", "\"Qty\" > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Sku).HasColumnType("citext").IsRequired();
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.HasIndex(x => x.CreditNoteId);
        builder.HasIndex(x => x.InvoiceLineId);
        builder.HasIndex(x => new { x.MarketCode, x.CreditNoteId }).HasDatabaseName("IX_invoices_credit_note_lines_market_cn");
    }
}

public sealed class InvoiceRenderJobConfiguration : IEntityTypeConfiguration<InvoiceRenderJob>
{
    public void Configure(EntityTypeBuilder<InvoiceRenderJob> builder)
    {
        builder.ToTable("invoice_render_jobs", "invoices", t =>
        {
            t.HasCheckConstraint("CK_invoices_render_jobs_state_enum",
                "\"State\" IN ('queued','rendering','done','failed')");
            t.HasCheckConstraint("CK_invoices_render_jobs_target_xor",
                "(\"InvoiceId\" IS NOT NULL)::int + (\"CreditNoteId\" IS NOT NULL)::int = 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.State).HasColumnType("citext").IsRequired().HasDefaultValue(InvoiceRenderJob.StateQueued);
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        // CR Major fix — include 'rendering' so a worker crash doesn't strand the row.
        builder.HasIndex(x => new { x.State, x.NextAttemptAt })
            .HasFilter("\"State\" IN ('queued','failed','rendering')")
            .HasDatabaseName("IX_invoices_render_jobs_pending");
        builder.HasIndex(x => new { x.MarketCode, x.NextAttemptAt }).HasDatabaseName("IX_invoices_render_jobs_market_attempt");
    }
}

public sealed class InvoiceTemplateConfiguration : IEntityTypeConfiguration<InvoiceTemplate>
{
    public void Configure(EntityTypeBuilder<InvoiceTemplate> builder)
    {
        builder.ToTable("invoice_templates", "invoices");
        builder.HasKey(x => x.MarketCode);
        builder.Property(x => x.MarketCode).HasColumnType("citext");
        builder.Property(x => x.BankDetailsJson).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
    }
}

public sealed class InvoicesOutboxEntryConfiguration : IEntityTypeConfiguration<InvoicesOutboxEntry>
{
    public void Configure(EntityTypeBuilder<InvoicesOutboxEntry> builder)
    {
        builder.ToTable("invoices_outbox", "invoices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasColumnType("citext").IsRequired();
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.HasIndex(x => x.CommittedAt)
            .HasFilter("\"DispatchedAt\" IS NULL")
            .HasDatabaseName("IX_invoices_outbox_pending");
        builder.HasIndex(x => x.AggregateId);
        builder.HasIndex(x => new { x.MarketCode, x.CommittedAt }).HasDatabaseName("IX_invoices_outbox_market_committed");
    }
}

public sealed class SubscriptionCheckpointConfiguration : IEntityTypeConfiguration<SubscriptionCheckpoint>
{
    public void Configure(EntityTypeBuilder<SubscriptionCheckpoint> builder)
    {
        builder.ToTable("subscription_checkpoints", "invoices");
        builder.HasKey(x => new { x.SourceModule, x.EventType });
        builder.Property(x => x.SourceModule).HasColumnType("citext");
        builder.Property(x => x.EventType).HasColumnType("citext");
    }
}
