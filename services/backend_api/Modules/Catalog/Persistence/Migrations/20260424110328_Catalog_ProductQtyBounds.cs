using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Catalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Catalog_ProductQtyBounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxPerOrder",
                schema: "catalog",
                table: "products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinOrderQty",
                schema: "catalog",
                table: "products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_products_max_per_order_non_negative",
                schema: "catalog",
                table: "products",
                sql: "\"MaxPerOrder\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_products_min_order_qty_non_negative",
                schema: "catalog",
                table: "products",
                sql: "\"MinOrderQty\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_products_qty_bounds_consistent",
                schema: "catalog",
                table: "products",
                sql: "\"MaxPerOrder\" = 0 OR \"MaxPerOrder\" >= \"MinOrderQty\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_products_max_per_order_non_negative",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_products_min_order_qty_non_negative",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_products_qty_bounds_consistent",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "MaxPerOrder",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "MinOrderQty",
                schema: "catalog",
                table: "products");
        }
    }
}
