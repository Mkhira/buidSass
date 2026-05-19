# Contracts — Phase 1: Auth & Identity

This phase consumes one OpenAPI document. No copies live here — the file is the source of truth.

## Sources

- [`services/backend_api/openapi.identity.json`](../../../../services/backend_api/openapi.identity.json) — 33 paths total; this phase consumes the 14 customer-tagged ones (see `../spec.md` §5).
- Backend module: [`services/backend_api/Modules/Identity`](../../../../services/backend_api/Modules/Identity)
- Backend spec: [`specs/phase-1B/004-identity-and-access/contracts/identity-and-access-contract.md`](../../../../specs/phase-1B/004-identity-and-access/contracts/identity-and-access-contract.md)

## Mobile-callable endpoints (14)

```
POST   /v1/customer/identity/register
POST   /v1/customer/identity/sign-in
POST   /v1/customer/identity/sign-out
POST   /v1/customer/identity/session/refresh
GET    /v1/customer/identity/me
PATCH  /v1/customer/identity/locale
POST   /v1/customer/identity/otp/request
POST   /v1/customer/identity/otp/verify
POST   /v1/customer/identity/password/reset-request
POST   /v1/customer/identity/password/reset-complete
POST   /v1/customer/identity/password/change
POST   /v1/customer/identity/email/confirm
GET    /v1/customer/identity/sessions
DELETE /v1/customer/identity/sessions/{sessionId}
```

## Explicitly NOT consumed by Phase 1

- All `/v1/admin/identity/*` — admin web app surface.
- `/v1/customer/identity/_test/protected` — test scaffolding, no UI binding.
