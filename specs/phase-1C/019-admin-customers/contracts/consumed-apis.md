# Consumed APIs

UI-only spec (FR-026). Every contract is **owned by another spec**.

| Surface | Owning spec | OpenAPI source | Wrapper |
|---|---|---|---|
| Customers list / profile / search | 004 identity | `services/backend_api/openapi.identity.json` | `apps/admin_web/lib/api/clients/identity.ts` (extends spec 015 / 018 baseline) |
| Suspend / unlock / password-reset-trigger | 004 identity | same | same |
| B2B company hierarchy (parent / branches / members) | 004 identity | same | same |
| Customer addresses (read-only) | 004 identity | same | same |
| Step-up MFA challenge + complete | 004 identity | same | spec 015's shared `<StepUpDialog>` calls `/api/auth/step-up/...` route handlers |
| Orders summary (count + most-recent) | 011 orders | `services/backend_api/openapi.orders.json` | `apps/admin_web/lib/api/clients/orders.ts` (shared with spec 018) |
| Verification history | 020 verification | future `openapi.verification.json` | placeholder until 020 ships |
| Quote history | 021 b2b / quotes | future `openapi.b2b.json` | placeholder until 021 ships |
| Support tickets | 023 support | future `openapi.support.json` | placeholder until 023 ships |
| Audit emissions (read-only via spec 015's reader) | 003 + spec 015 | `openapi.json` | spec 015's `audit.ts` wrapper |

## Auth proxy boundary

Inherits spec 015's auth-proxy. Every browser request goes through Next.js Route Handlers under `app/api/customers/...`, `app/api/auth/step-up/...`. The customer-search endpoint specifically is **not** logged client-side or server-side beyond the standard request log — the search query string is PII-sensitive.

## Headers attached

Same as spec 015 / 018. Account actions add:

| Header | Source | Purpose |
|---|---|---|
| `Idempotency-Key: <uuid>` | account-action draft | Spec 004 idempotency on suspend / unlock / password-reset-trigger |
| `X-StepUp-Assertion: <id>` | step-up dialog success | Required by spec 004 for these actions |

## Escalation policy

Every backend gap discovered during implementation is **never patched here** (FR-026). File issues against spec 004 (or 020 / 021 / 023 when their panels are wired) using `spec-XXX:gap:<short-description>`.

## Specific spec-004 gaps anticipated

- **Customer-search endpoint** — confirm the index supports email + phone + display-name partial match within the admin's role scope, and that PII is server-redacted for admins lacking `customers.pii.read`. If not, file `spec-004:gap:customer-search-pii-redaction`.
- **Suspend cascade endpoint** — confirm a single endpoint atomically flips `accountState` + revokes sessions. If the cascade requires two calls (suspend + revoke-sessions), the UI files `spec-004:gap:suspend-cascade-atomicity` and acts only on the suspend call (the revoke step happens server-side as a follow-up worker).
- **Step-up assertion id** — confirm spec 004 verifies the assertion id forwarded by spec 019's account-action POSTs.
