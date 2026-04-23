using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Search.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Search_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "search");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "reindex_jobs",
                schema: "search",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IndexName = table.Column<string>(type: "citext", nullable: false),
                    Status = table.Column<string>(type: "citext", nullable: false),
                    StartedByAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DocsExpected = table.Column<int>(type: "integer", nullable: true),
                    DocsWritten = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reindex_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "search_indexer_cursor",
                schema: "search",
                columns: table => new
                {
                    IndexName = table.Column<string>(type: "citext", nullable: false),
                    OutboxLastIdApplied = table.Column<long>(type: "bigint", nullable: false),
                    LastSuccessAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LagSecondsLastObserved = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_indexer_cursor", x => x.IndexName);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reindex_jobs_IndexName",
                schema: "search",
                table: "reindex_jobs",
                column: "IndexName",
                unique: true,
                filter: "\"Status\" IN ('pending','running')");

            migrationBuilder.CreateIndex(
                name: "IX_reindex_jobs_IndexName_Status",
                schema: "search",
                table: "reindex_jobs",
                columns: new[] { "IndexName", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_reindex_jobs_StartedAt",
                schema: "search",
                table: "reindex_jobs",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_search_indexer_cursor_UpdatedAt",
                schema: "search",
                table: "search_indexer_cursor",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reindex_jobs",
                schema: "search");

            migrationBuilder.DropTable(
                name: "search_indexer_cursor",
                schema: "search");
        }
    }
}
