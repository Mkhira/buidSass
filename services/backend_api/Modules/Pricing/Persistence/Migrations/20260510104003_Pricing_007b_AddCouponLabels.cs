using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Pricing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Pricing_007b_AddCouponLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AuthorActorId",
                schema: "pricing",
                table: "coupons",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "DescriptionAr",
                schema: "pricing",
                table: "coupons",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionEn",
                schema: "pricing",
                table: "coupons",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LabelAr",
                schema: "pricing",
                table: "coupons",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LabelEn",
                schema: "pricing",
                table: "coupons",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthorActorId",
                schema: "pricing",
                table: "coupons");

            migrationBuilder.DropColumn(
                name: "DescriptionAr",
                schema: "pricing",
                table: "coupons");

            migrationBuilder.DropColumn(
                name: "DescriptionEn",
                schema: "pricing",
                table: "coupons");

            migrationBuilder.DropColumn(
                name: "LabelAr",
                schema: "pricing",
                table: "coupons");

            migrationBuilder.DropColumn(
                name: "LabelEn",
                schema: "pricing",
                table: "coupons");
        }
    }
}
