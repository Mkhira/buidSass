# Backend API

## Database Application Role

Shared foundations use a dedicated PostgreSQL application role:

- Role name: `dental_api_app`

Default privileges baseline:

- `CONNECT` on database
- `USAGE` and `SELECT` on schema/table scope needed for reads
- `INSERT` granted only where module migrations explicitly allow writes
- `UPDATE`/`DELETE` are explicitly revoked on append-only audit artifacts

Module migrations and grant/revoke scripts (including audit-log `RevokeAuditWriteGrants.sql`) must reference this role name.
