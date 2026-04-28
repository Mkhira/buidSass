# Consumed APIs

UI-only spec (FR-024). Every contract is **owned by another spec**.

| Surface | Owning spec | OpenAPI source | Wrapper |
|---|---|---|---|
| Orders list / detail / state-transitions / timeline / export | 011 orders | `services/backend_api/openapi.orders.json` | `apps/admin_web/lib/api/clients/orders.ts` |
| Tax-invoice render-status / download / regenerate | 012 tax-invoices | `services/backend_api/openapi.invoices.json` | `apps/admin_web/lib/api/clients/invoices.ts` |
| Refund initiation + over-refund guard | 013 returns | `services/backend_api/openapi.returns.json` | `apps/admin_web/lib/api/clients/returns.ts` |
| Step-up MFA challenge + complete | 004 identity | `services/backend_api/openapi.identity.json` | spec 015's `apps/admin_web/lib/api/clients/identity.ts` |
| Storage signed-URL issuance for invoice + export downloads | 003 storage abstraction | `openapi.json` | spec 015 / 016's `lib/api/clients/storage.ts` |
| Audit emissions (read-only via spec 015's reader) | 003 + spec 015 | `openapi.json` | spec 015's `audit.ts` wrapper |

## Auth proxy boundary

Inherits spec 015's auth-proxy completely. Every browser request for orders / invoices / refunds / step-up / exports goes through a Next.js Route Handler under `app/api/orders/...`, `app/api/invoices/...`, `app/api/refunds/...`, etc.

## Headers attached

Same as spec 015, plus:

| Header | Source | Purpose |
|---|---|---|
| `Idempotency-Key: <uuid>` | refund / state-transition forms | Spec 011 / 013 idempotency |
| `X-StepUp-Assertion: <id>` | refund submit when step-up was required | Carries the spec 004 step-up assertion id |

## Escalation policy

Every backend gap discovered during implementation is **never patched here** (FR-024). File issues against specs 011 / 012 / 013 / 004 / 003 with the prefix `spec-XXX:gap:<short-description>`. Cross-link from the relevant `apps/admin_web/components/orders/` TODO comment.
