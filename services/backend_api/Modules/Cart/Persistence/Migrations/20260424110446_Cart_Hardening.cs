using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Cart.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Cart_Hardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_carts_token_hash_active",
                schema: "cart",
                table: "carts");

            migrationBuilder.AddColumn<int>(
                name: "Qty",
                schema: "cart",
                table: "cart_saved_items",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_carts_token_hash_active",
                schema: "cart",
                table: "carts",
                column: "CartTokenHash",
                unique: true,
                filter: "\"Status\" = 'active' AND \"CartTokenHash\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_carts_identity_present",
                schema: "cart",
                table: "carts",
                sql: "\"AccountId\" IS NOT NULL OR \"CartTokenHash\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_carts_status_enum",
                schema: "cart",
                table: "carts",
                sql: "\"Status\" IN ('active','archived','merged','purged')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_cart_saved_items_qty_positive",
                schema: "cart",
                table: "cart_saved_items",
                sql: "\"Qty\" >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_carts_token_hash_active",
                schema: "cart",
                table: "carts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_carts_identity_present",
                schema: "cart",
                table: "carts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_carts_status_enum",
                schema: "cart",
                table: "carts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_cart_saved_items_qty_positive",
                schema: "cart",
                table: "cart_saved_items");

            migrationBuilder.DropColumn(
                name: "Qty",
                schema: "cart",
                table: "cart_saved_items");

            // Down restores Cart_Initial's NON-unique partial index shape. Up() dropped that
            // index and created a unique one; Down's job is to undo Up, not to mirror Up.
            migrationBuilder.CreateIndex(
                name: "IX_carts_token_hash_active",
                schema: "cart",
                table: "carts",
                column: "CartTokenHash",
                filter: "\"Status\" = 'active' AND \"CartTokenHash\" IS NOT NULL");
        }
    }
}
