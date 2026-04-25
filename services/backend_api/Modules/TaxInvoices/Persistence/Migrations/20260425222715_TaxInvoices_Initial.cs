using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BackendApi.Modules.TaxInvoices.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaxInvoices_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "invoices");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "invoice_render_jobs",
                schema: "invoices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreditNoteId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<string>(type: "citext", nullable: false, defaultValue: "queued"),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_render_jobs", x => x.Id);
                    table.CheckConstraint("CK_invoices_render_jobs_state_enum", "\"State\" IN ('queued','rendering','done','failed')");
                    table.CheckConstraint("CK_invoices_render_jobs_target_xor", "(\"InvoiceId\" IS NOT NULL)::int + (\"CreditNoteId\" IS NOT NULL)::int = 1");
                });

            migrationBuilder.CreateTable(
                name: "invoice_templates",
                schema: "invoices",
                columns: table => new
                {
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    SellerLegalNameAr = table.Column<string>(type: "text", nullable: false),
                    SellerLegalNameEn = table.Column<string>(type: "text", nullable: false),
                    SellerVatNumber = table.Column<string>(type: "text", nullable: false),
                    SellerAddressAr = table.Column<string>(type: "text", nullable: false),
                    SellerAddressEn = table.Column<string>(type: "text", nullable: false),
                    BankDetailsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    FooterHtmlAr = table.Column<string>(type: "text", nullable: true),
                    FooterHtmlEn = table.Column<string>(type: "text", nullable: true),
                    UpdatedByAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_templates", x => x.MarketCode);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "text", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    Currency = table.Column<string>(type: "citext", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PriceExplanationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubtotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    DiscountMinor = table.Column<long>(type: "bigint", nullable: false),
                    TaxMinor = table.Column<long>(type: "bigint", nullable: false),
                    ShippingMinor = table.Column<long>(type: "bigint", nullable: false),
                    GrandTotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    BillToJson = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    SellerJson = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    B2bPoNumber = table.Column<string>(type: "text", nullable: true),
                    PdfBlobKey = table.Column<string>(type: "text", nullable: true),
                    PdfSha256 = table.Column<string>(type: "text", nullable: true),
                    XmlBlobKey = table.Column<string>(type: "text", nullable: true),
                    ZatcaQrB64 = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<string>(type: "citext", nullable: false, defaultValue: "pending"),
                    RenderAttempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoices", x => x.Id);
                    table.CheckConstraint("CK_invoices_invoices_grand_total_non_negative", "\"GrandTotalMinor\" >= 0");
                    table.CheckConstraint("CK_invoices_invoices_state_enum", "\"State\" IN ('pending','rendered','delivered','failed')");
                });

            migrationBuilder.CreateTable(
                name: "invoices_outbox",
                schema: "invoices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventType = table.Column<string>(type: "citext", nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    CommittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoices_outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "subscription_checkpoints",
                schema: "invoices",
                columns: table => new
                {
                    SourceModule = table.Column<string>(type: "citext", nullable: false),
                    EventType = table.Column<string>(type: "citext", nullable: false),
                    LastObservedOutboxId = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_checkpoints", x => new { x.SourceModule, x.EventType });
                });

            migrationBuilder.CreateTable(
                name: "credit_notes",
                schema: "invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditNoteNumber = table.Column<string>(type: "text", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefundId = table.Column<Guid>(type: "uuid", nullable: true),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubtotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    DiscountMinor = table.Column<long>(type: "bigint", nullable: false),
                    TaxMinor = table.Column<long>(type: "bigint", nullable: false),
                    ShippingMinor = table.Column<long>(type: "bigint", nullable: false),
                    GrandTotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    ReasonCode = table.Column<string>(type: "citext", nullable: false),
                    PdfBlobKey = table.Column<string>(type: "text", nullable: true),
                    PdfSha256 = table.Column<string>(type: "text", nullable: true),
                    ZatcaQrB64 = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<string>(type: "citext", nullable: false, defaultValue: "pending"),
                    RenderAttempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_notes", x => x.Id);
                    table.CheckConstraint("CK_invoices_credit_notes_state_enum", "\"State\" IN ('pending','rendered','delivered','failed')");
                    table.ForeignKey(
                        name: "FK_credit_notes_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "invoices",
                        principalTable: "invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                schema: "invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "citext", nullable: false),
                    NameAr = table.Column<string>(type: "text", nullable: false),
                    NameEn = table.Column<string>(type: "text", nullable: false),
                    Qty = table.Column<int>(type: "integer", nullable: false),
                    UnitPriceMinor = table.Column<long>(type: "bigint", nullable: false),
                    LineDiscountMinor = table.Column<long>(type: "bigint", nullable: false),
                    LineTaxMinor = table.Column<long>(type: "bigint", nullable: false),
                    LineTotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    TaxRateBp = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_lines", x => x.Id);
                    table.CheckConstraint("CK_invoices_invoice_lines_qty_positive", "\"Qty\" > 0");
                    table.CheckConstraint("CK_invoices_invoice_lines_tax_rate_bp_bounds", "\"TaxRateBp\" >= 0 AND \"TaxRateBp\" <= 10000");
                    table.ForeignKey(
                        name: "FK_invoice_lines_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "invoices",
                        principalTable: "invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "credit_note_lines",
                schema: "invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditNoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "citext", nullable: false),
                    NameAr = table.Column<string>(type: "text", nullable: false),
                    NameEn = table.Column<string>(type: "text", nullable: false),
                    Qty = table.Column<int>(type: "integer", nullable: false),
                    UnitPriceMinor = table.Column<long>(type: "bigint", nullable: false),
                    LineDiscountMinor = table.Column<long>(type: "bigint", nullable: false),
                    LineTaxMinor = table.Column<long>(type: "bigint", nullable: false),
                    LineTotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    TaxRateBp = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_note_lines", x => x.Id);
                    table.CheckConstraint("CK_invoices_credit_note_lines_qty_positive", "\"Qty\" > 0");
                    table.ForeignKey(
                        name: "FK_credit_note_lines_credit_notes_CreditNoteId",
                        column: x => x.CreditNoteId,
                        principalSchema: "invoices",
                        principalTable: "credit_notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_credit_note_lines_CreditNoteId",
                schema: "invoices",
                table: "credit_note_lines",
                column: "CreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_note_lines_InvoiceLineId",
                schema: "invoices",
                table: "credit_note_lines",
                column: "InvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_notes_CreditNoteNumber",
                schema: "invoices",
                table: "credit_notes",
                column: "CreditNoteNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_credit_notes_InvoiceId",
                schema: "invoices",
                table: "credit_notes",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_credit_notes_refund_unique",
                schema: "invoices",
                table: "credit_notes",
                column: "RefundId",
                unique: true,
                filter: "\"RefundId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_lines_InvoiceId",
                schema: "invoices",
                table: "invoice_lines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_render_jobs_pending",
                schema: "invoices",
                table: "invoice_render_jobs",
                columns: new[] { "State", "NextAttemptAt" },
                filter: "\"State\" IN ('queued','failed')");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_InvoiceNumber",
                schema: "invoices",
                table: "invoices",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoices_invoices_account_issued",
                schema: "invoices",
                table: "invoices",
                columns: new[] { "AccountId", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_invoices_invoices_market_issued",
                schema: "invoices",
                table: "invoices",
                columns: new[] { "MarketCode", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_invoices_invoices_order",
                schema: "invoices",
                table: "invoices",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_invoices_state",
                schema: "invoices",
                table: "invoices",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_outbox_AggregateId",
                schema: "invoices",
                table: "invoices_outbox",
                column: "AggregateId");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_outbox_pending",
                schema: "invoices",
                table: "invoices_outbox",
                column: "CommittedAt",
                filter: "\"DispatchedAt\" IS NULL");

            // B3 — seed launch invoice templates (FR-017). Idempotent on re-apply via the
            // primary key ON CONFLICT clause; admin overrides via UpdateInvoiceTemplate are
            // preserved.
            migrationBuilder.Sql(@"
                INSERT INTO invoices.invoice_templates
                    (""MarketCode"", ""SellerLegalNameAr"", ""SellerLegalNameEn"",
                     ""SellerVatNumber"", ""SellerAddressAr"", ""SellerAddressEn"",
                     ""BankDetailsJson"", ""FooterHtmlAr"", ""FooterHtmlEn"",
                     ""UpdatedByAccountId"", ""UpdatedAt"")
                VALUES
                    ('KSA',
                     'منصة تجارة الأسنان المحدودة',
                     'Dental Commerce Platform LLC',
                     '300000000000003',
                     'الرياض، المملكة العربية السعودية',
                     'Riyadh, Kingdom of Saudi Arabia',
                     '{""bankNameAr"":""البنك الأهلي السعودي"",""bankNameEn"":""Saudi National Bank"",""iban"":""SA0000000000000000000000"",""accountHolder"":""Dental Commerce Platform LLC""}'::jsonb,
                     '<p>هذه فاتورة ضريبية صادرة وفقاً لأنظمة هيئة الزكاة والضريبة والجمارك.</p>',
                     '<p>This is a tax invoice issued in accordance with ZATCA regulations.</p>',
                     NULL, NOW()),
                    ('EG',
                     'منصة تجارة الأسنان (مصر) ش.م.م',
                     'Dental Commerce Platform (Egypt) S.A.E.',
                     'EG-VAT-PENDING',
                     'القاهرة، جمهورية مصر العربية',
                     'Cairo, Arab Republic of Egypt',
                     '{""bankNameAr"":""البنك التجاري الدولي"",""bankNameEn"":""Commercial International Bank"",""iban"":""EG000000000000000000000000000"",""accountHolder"":""Dental Commerce Platform (Egypt) S.A.E.""}'::jsonb,
                     '<p>فاتورة ضريبية داخلية - للتسجيل المحاسبي.</p>',
                     '<p>Internal tax invoice — for accounting records.</p>',
                     NULL, NOW())
                ON CONFLICT (""MarketCode"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_note_lines",
                schema: "invoices");

            migrationBuilder.DropTable(
                name: "invoice_lines",
                schema: "invoices");

            migrationBuilder.DropTable(
                name: "invoice_render_jobs",
                schema: "invoices");

            migrationBuilder.DropTable(
                name: "invoice_templates",
                schema: "invoices");

            migrationBuilder.DropTable(
                name: "invoices_outbox",
                schema: "invoices");

            migrationBuilder.DropTable(
                name: "subscription_checkpoints",
                schema: "invoices");

            migrationBuilder.DropTable(
                name: "credit_notes",
                schema: "invoices");

            migrationBuilder.DropTable(
                name: "invoices",
                schema: "invoices");
        }
    }
}
