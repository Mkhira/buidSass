# Quickstart — Identity and Access (004)

**Feature**: `specs/phase-1B/004-identity-and-access/spec.md`
**Plan**: `./plan.md`

## 1. Prerequisites

- .NET 9 SDK
- Docker (for Testcontainers Postgres)
- `scripts/compute-fingerprint.sh` available (from spec 001)
- Phase 1A specs 001–003 at DoD on `main`

## 2. Bring the module up locally

```bash
# From repo root
cd services/backend_api

# 1. Apply migrations against a local dev Postgres (configured in appsettings.Development.json)
dotnet ef database update --project Features/Identity --context IdentityDbContext

# 2. Run the RBAC seed (super-admin, seed roles, seed permissions)
dotnet run --project Tools/SeedRoles -- --env=Development

# 3. Start the API with the TestOtpProvider wired
Identity__OtpProvider=test dotnet run --project Api
```

## 3. Walk the P1 acceptance scenarios (from spec)

### 3.1 Register a customer (Story 1, AS-1)

```bash
curl -X POST http://localhost:5000/customers/register \
  -H 'Content-Type: application/json' \
  -d '{"email":"buyer+1@example.com","password":"S3cure-pass-example!","market":"KSA","locale":"en"}'
# → 202 { customerId, channel: "email", resendAllowedAt }
```

The TestOtpProvider writes the dispatched OTP to an in-memory buffer readable at:

```bash
curl http://localhost:5000/test/otp/last?channel=email&target=buyer+1@example.com
# → { code: "123456", issuedAt, expiresAt }  (Development environment only)
```

### 3.2 Verify the contact (Story 1, AS-3)

```bash
curl -X POST http://localhost:5000/customers/verify-contact \
  -H 'Content-Type: application/json' \
  -d '{"customerId":"<id>","code":"123456"}'
# → 200 { accessToken, refreshToken, sessionId, ... }
```

### 3.3 Login, refresh, logout (Story 2)

```bash
# Login
curl -X POST http://localhost:5000/customers/login \
  -H 'Content-Type: application/json' \
  -d '{"identifier":"buyer+1@example.com","password":"S3cure-pass-example!"}'

# Refresh
curl -X POST http://localhost:5000/customers/refresh \
  -H 'Content-Type: application/json' \
  -d '{"refreshToken":"<rt>"}'

# Logout
curl -X POST http://localhost:5000/customers/logout \
  -H 'Authorization: Bearer <at>'
```

### 3.4 Admin single-session newest-wins (Story 4 / FR-011b)

```bash
# First login as an admin
curl -X POST http://localhost:5000/admins/login \
  -H 'Content-Type: application/json' \
  -d '{"identifier":"super-admin@example.com","password":"<seeded-password>"}'
# → session A

# Second login for the same admin (simulates another device)
curl -X POST http://localhost:5000/admins/login -d '{"identifier":"super-admin@example.com","password":"<seeded-password>"}'
# → session B; session A is revoked within the same transaction.

# Attempting to refresh session A now fails with 401
curl -X POST http://localhost:5000/admins/refresh -d '{"refreshToken":"<rt-A>"}'
# → 401
```

### 3.5 RBAC matrix (Story 5)

```bash
# Seed a catalog-editor admin
dotnet run --project Tools/SeedSample -- --admin=catalog-editor

# Login as catalog-editor, call a catalog-protected fixture endpoint
curl -X GET http://localhost:5000/internal/fixture/catalog-read -H 'Authorization: Bearer <at>'
# → 200

curl -X POST http://localhost:5000/internal/fixture/catalog-write -H 'Authorization: Bearer <at>'
# → 403 with audit-log entry identity.admin.authorization.denied
```

### 3.6 OTP provider swap (SC-007)

```bash
# Switch to a second dummy provider via config (no code change)
Identity__OtpProvider=test-alt dotnet run --project Api

# Same endpoints, same payloads — Story 1 still passes.
```

## 4. Run the tests

```bash
# Unit
dotnet test services/backend_api/Tests/Identity.Unit

# Integration (Testcontainers spins up Postgres)
dotnet test services/backend_api/Tests/Identity.Integration

# Contract diff against prior published OpenAPI
pnpm --filter @buidsass/contracts contract-diff identity
```

## 5. Regenerate shared contracts

```bash
# From repo root
scripts/shared-contracts/generate.sh identity
# Writes packages/shared_contracts/identity/{dart,ts}/
```

## 6. Verify DoD

- [ ] All FR-00X acceptance scenarios run green (3.1–3.5 above + their negative twins).
- [ ] AR + EN editorial sign-off recorded in `specs/phase-1B/004-identity-and-access/checklists/editorial-signoff.md`.
- [ ] k6 script `tests/perf/identity/login.k6.js` reports p95 ≤ 1 s at baseline load.
- [ ] Argon2id verify timing benchmark reports ≥ 100 ms on baseline vCPU (`dotnet run --project Tools/Bench -- argon2id-verify`).
- [ ] Contract fingerprint appended to PR body via `scripts/compute-fingerprint.sh`.
- [ ] No new secrets committed (gitleaks CI green).
- [ ] Audit-log sink receives an event for every action in `contracts/events.md` — verified by integration test `AuditCoverageTests.AllEventsPublished`.
