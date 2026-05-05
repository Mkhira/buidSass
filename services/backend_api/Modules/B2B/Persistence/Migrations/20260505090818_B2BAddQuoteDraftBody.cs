using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.B2B.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B2BAddQuoteDraftBody : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "draft_body",
                schema: "b2b",
                table: "quotes",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "draft_body",
                schema: "b2b",
                table: "quotes");
        }
    }
}
