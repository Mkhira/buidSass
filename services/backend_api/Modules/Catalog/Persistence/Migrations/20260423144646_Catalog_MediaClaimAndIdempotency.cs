using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Catalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Catalog_MediaClaimAndIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VariantAttempts",
                schema: "catalog",
                table: "product_media",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VariantClaimedAt",
                schema: "catalog",
                table: "product_media",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bulk_import_idempotency",
                schema: "catalog",
                columns: table => new
                {
                    RowHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "citext", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bulk_import_idempotency", x => x.RowHash);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bulk_import_idempotency",
                schema: "catalog");

            migrationBuilder.DropColumn(
                name: "VariantAttempts",
                schema: "catalog",
                table: "product_media");

            migrationBuilder.DropColumn(
                name: "VariantClaimedAt",
                schema: "catalog",
                table: "product_media");
        }
    }
}
