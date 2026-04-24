using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Cart.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Cart_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cart");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "cart_abandoned_emissions",
                schema: "cart",
                columns: table => new
                {
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastEmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cart_abandoned_emissions", x => x.CartId);
                });

            migrationBuilder.CreateTable(
                name: "cart_b2b_metadata",
                schema: "cart",
                columns: table => new
                {
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    PoNumber = table.Column<string>(type: "text", nullable: true),
                    Reference = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    RequestedDeliveryFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RequestedDeliveryTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cart_b2b_metadata", x => x.CartId);
                });

            migrationBuilder.CreateTable(
                name: "cart_lines",
                schema: "cart",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Qty = table.Column<int>(type: "integer", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Unavailable = table.Column<bool>(type: "boolean", nullable: false),
                    Restricted = table.Column<bool>(type: "boolean", nullable: false),
                    RestrictionReasonCode = table.Column<string>(type: "citext", nullable: true),
                    StockChanged = table.Column<bool>(type: "boolean", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cart_lines", x => x.Id);
                    table.CheckConstraint("CK_cart_lines_qty_positive", "\"Qty\" >= 1");
                });

            migrationBuilder.CreateTable(
                name: "cart_saved_items",
                schema: "cart",
                columns: table => new
                {
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cart_saved_items", x => new { x.CartId, x.ProductId });
                });

            migrationBuilder.CreateTable(
                name: "carts",
                schema: "cart",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    CartTokenHash = table.Column<byte[]>(type: "bytea", nullable: true),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    Status = table.Column<string>(type: "citext", nullable: false),
                    CouponCode = table.Column<string>(type: "citext", nullable: true),
                    LastTouchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedReason = table.Column<string>(type: "citext", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    OwnerId = table.Column<string>(type: "citext", nullable: false, defaultValue: "platform"),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cart_abandoned_emissions_LastEmittedAt",
                schema: "cart",
                table: "cart_abandoned_emissions",
                column: "LastEmittedAt");

            migrationBuilder.CreateIndex(
                name: "IX_cart_lines_CartId",
                schema: "cart",
                table: "cart_lines",
                column: "CartId");

            migrationBuilder.CreateIndex(
                name: "IX_cart_lines_CartId_ProductId",
                schema: "cart",
                table: "cart_lines",
                columns: new[] { "CartId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_carts_account_market_active",
                schema: "cart",
                table: "carts",
                columns: new[] { "AccountId", "MarketCode" },
                unique: true,
                filter: "\"Status\" = 'active' AND \"AccountId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_carts_LastTouchedAt",
                schema: "cart",
                table: "carts",
                column: "LastTouchedAt");

            migrationBuilder.CreateIndex(
                name: "IX_carts_MarketCode",
                schema: "cart",
                table: "carts",
                column: "MarketCode");

            migrationBuilder.CreateIndex(
                name: "IX_carts_Status",
                schema: "cart",
                table: "carts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_carts_token_hash_active",
                schema: "cart",
                table: "carts",
                column: "CartTokenHash",
                filter: "\"Status\" = 'active' AND \"CartTokenHash\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cart_abandoned_emissions",
                schema: "cart");

            migrationBuilder.DropTable(
                name: "cart_b2b_metadata",
                schema: "cart");

            migrationBuilder.DropTable(
                name: "cart_lines",
                schema: "cart");

            migrationBuilder.DropTable(
                name: "cart_saved_items",
                schema: "cart");

            migrationBuilder.DropTable(
                name: "carts",
                schema: "cart");
        }
    }
}
