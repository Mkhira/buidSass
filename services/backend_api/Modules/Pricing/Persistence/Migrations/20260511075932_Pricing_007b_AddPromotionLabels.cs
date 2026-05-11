using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Pricing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Pricing_007b_AddPromotionLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AmountOffMinor",
                schema: "pricing",
                table: "promotions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AuthorActorId",
                schema: "pricing",
                table: "promotions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "BundleSku",
                schema: "pricing",
                table: "promotions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionAr",
                schema: "pricing",
                table: "promotions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionEn",
                schema: "pricing",
                table: "promotions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LabelAr",
                schema: "pricing",
                table: "promotions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LabelEn",
                schema: "pricing",
                table: "promotions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PercentOff",
                schema: "pricing",
                table: "promotions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RewardSku",
                schema: "pricing",
                table: "promotions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StacksWithCoupons",
                schema: "pricing",
                table: "promotions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "StacksWithOtherPromotions",
                schema: "pricing",
                table: "promotions",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AmountOffMinor",
                schema: "pricing",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "AuthorActorId",
                schema: "pricing",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "BundleSku",
                schema: "pricing",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "DescriptionAr",
                schema: "pricing",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "DescriptionEn",
                schema: "pricing",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "LabelAr",
                schema: "pricing",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "LabelEn",
                schema: "pricing",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "PercentOff",
                schema: "pricing",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "RewardSku",
                schema: "pricing",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "StacksWithCoupons",
                schema: "pricing",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "StacksWithOtherPromotions",
                schema: "pricing",
                table: "promotions");
        }
    }
}
