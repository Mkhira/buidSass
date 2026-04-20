# Backend API

## Database Application Role

Shared foundations use a dedicated PostgreSQL application role:

- Role name: `dental_api_app`

Default privileges baseline:

- `CONNECT` on database
- `USAGE` and `SELECT` on schema/table scope needed for reads
- `INSERT` granted only where module migrations explicitly allow writes
- `UPDATE`/`DELETE` are explicitly revoked on append-only audit artifacts

Module migrations that touch role-level privileges MUST reference this role name. The `dental_api_app` role is provisioned out-of-band; grant changes are applied through EF migrations under `services/backend_api/Migrations/` (see `20260419190000_RevokeAuditWriteGrants.cs` for the audit-log INSERT-only enforcement).
