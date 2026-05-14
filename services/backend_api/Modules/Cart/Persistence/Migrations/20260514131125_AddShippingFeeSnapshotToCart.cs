using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Cart.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingFeeSnapshotToCart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ShippingFeeAmountSnapshot",
                schema: "cart",
                table: "carts",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ShippingFeeSnapshotAt",
                schema: "cart",
                table: "carts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingFeeSnapshotJson",
                schema: "cart",
                table: "carts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ShippingMethodVersionIdSnapshot",
                schema: "cart",
                table: "carts",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShippingFeeAmountSnapshot",
                schema: "cart",
                table: "carts");

            migrationBuilder.DropColumn(
                name: "ShippingFeeSnapshotAt",
                schema: "cart",
                table: "carts");

            migrationBuilder.DropColumn(
                name: "ShippingFeeSnapshotJson",
                schema: "cart",
                table: "carts");

            migrationBuilder.DropColumn(
                name: "ShippingMethodVersionIdSnapshot",
                schema: "cart",
                table: "carts");
        }
    }
}
