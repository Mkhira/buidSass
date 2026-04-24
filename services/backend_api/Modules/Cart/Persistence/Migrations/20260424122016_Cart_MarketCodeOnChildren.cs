using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Cart.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Cart_MarketCodeOnChildren : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MarketCode",
                schema: "cart",
                table: "cart_saved_items",
                type: "citext",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MarketCode",
                schema: "cart",
                table: "cart_lines",
                type: "citext",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MarketCode",
                schema: "cart",
                table: "cart_b2b_metadata",
                type: "citext",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MarketCode",
                schema: "cart",
                table: "cart_abandoned_emissions",
                type: "citext",
                nullable: false,
                defaultValue: "");

            // Backfill child MarketCode from the owning cart so rows persisted under the prior
            // schema don't end up as blank-market ('') entries that partition-scoped queries miss.
            // Runs after AddColumn — all four tables have the column present + a (possibly empty)
            // default at this point, and the parent cart.carts.MarketCode is already non-null.
            migrationBuilder.Sql(@"
                UPDATE cart.cart_saved_items s
                SET ""MarketCode"" = c.""MarketCode""
                FROM cart.carts c
                WHERE s.""CartId"" = c.""Id"" AND s.""MarketCode"" = '';");

            migrationBuilder.Sql(@"
                UPDATE cart.cart_lines l
                SET ""MarketCode"" = c.""MarketCode""
                FROM cart.carts c
                WHERE l.""CartId"" = c.""Id"" AND l.""MarketCode"" = '';");

            migrationBuilder.Sql(@"
                UPDATE cart.cart_b2b_metadata b
                SET ""MarketCode"" = c.""MarketCode""
                FROM cart.carts c
                WHERE b.""CartId"" = c.""Id"" AND b.""MarketCode"" = '';");

            migrationBuilder.Sql(@"
                UPDATE cart.cart_abandoned_emissions e
                SET ""MarketCode"" = c.""MarketCode""
                FROM cart.carts c
                WHERE e.""CartId"" = c.""Id"" AND e.""MarketCode"" = '';");

            migrationBuilder.CreateIndex(
                name: "IX_cart_saved_items_MarketCode",
                schema: "cart",
                table: "cart_saved_items",
                column: "MarketCode");

            migrationBuilder.CreateIndex(
                name: "IX_cart_lines_MarketCode",
                schema: "cart",
                table: "cart_lines",
                column: "MarketCode");

            migrationBuilder.CreateIndex(
                name: "IX_cart_b2b_metadata_MarketCode",
                schema: "cart",
                table: "cart_b2b_metadata",
                column: "MarketCode");

            migrationBuilder.CreateIndex(
                name: "IX_cart_abandoned_emissions_MarketCode",
                schema: "cart",
                table: "cart_abandoned_emissions",
                column: "MarketCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_cart_saved_items_MarketCode",
                schema: "cart",
                table: "cart_saved_items");

            migrationBuilder.DropIndex(
                name: "IX_cart_lines_MarketCode",
                schema: "cart",
                table: "cart_lines");

            migrationBuilder.DropIndex(
                name: "IX_cart_b2b_metadata_MarketCode",
                schema: "cart",
                table: "cart_b2b_metadata");

            migrationBuilder.DropIndex(
                name: "IX_cart_abandoned_emissions_MarketCode",
                schema: "cart",
                table: "cart_abandoned_emissions");

            migrationBuilder.DropColumn(
                name: "MarketCode",
                schema: "cart",
                table: "cart_saved_items");

            migrationBuilder.DropColumn(
                name: "MarketCode",
                schema: "cart",
                table: "cart_lines");

            migrationBuilder.DropColumn(
                name: "MarketCode",
                schema: "cart",
                table: "cart_b2b_metadata");

            migrationBuilder.DropColumn(
                name: "MarketCode",
                schema: "cart",
                table: "cart_abandoned_emissions");
        }
    }
}
