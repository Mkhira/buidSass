using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Enforces the INSERT-only guarantee for audit_log_entries at the DB level.
    /// Constitution Principle 25 requires audit trails to be tamper-evident; revoking
    /// UPDATE/DELETE from the app role makes the guarantee mechanical, not a matter of
    /// discipline. Role name is intentionally hardcoded to "dental_api_app" — see
    /// services/backend_api/README.md for provisioning.
    /// </remarks>
    public partial class RevokeAuditWriteGrants : Migration
    {
        private const string AppRole = "dental_api_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        REVOKE UPDATE, DELETE ON TABLE audit_log_entries FROM {AppRole};
                        GRANT INSERT, SELECT ON TABLE audit_log_entries TO {AppRole};
                    END IF;
                END$$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        GRANT UPDATE, DELETE ON TABLE audit_log_entries TO {AppRole};
                    END IF;
                END$$;
            ");
        }
    }
}
