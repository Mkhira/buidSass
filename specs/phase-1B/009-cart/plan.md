# Implementation Plan — Cart v1 (Spec 009)

**Branch**: `phase-1B-specs` · **Date**: 2026-04-22

## Technical Context
- **Runtime**: .NET 9 / C# 12.
- **DB**: PostgreSQL 16; schema `cart`.
- **Module**: `services/backend_api/Modules/Cart/`.
- **Deps**: EF Core (shared), FluentValidation.

## Constitution Check
| Principle | Gate | Note |
|---|---|---|
| 3 — Browse without auth | PASS | Anon carts via `cart_token`. |
| 5 — Market-configurable | PASS | One cart per `(account, market)`. |
| 6 — Multi-vendor-ready | PASS | `owner_id/vendor_id` on cart (defaulted to platform). |
| 8 — Restricted visibility | PASS | Restricted products addable; eligibility surfaced but never hidden. |
| 9 — B2B | PASS | B2B cart metadata fields + `requested_delivery_window`. |
| 10 — Centralized pricing | PASS | Cart calls spec 007-a Preview on every read. |
| 11 — Inventory depth | PASS | Soft reservation via spec 008. |
| 17 — Order separation | PASS | Cart does not persist totals; checkout (spec 010) does. |
| 22/23 — Stack + architecture | PASS | .NET + Postgres; module slice. |
| 24 — State machines | PASS | Cart lifecycle enumerated. |
| 25 — Audit | PASS | Admin access audited; customer mutations logged. |
| 27 — UX quality | PASS | Full breakdown returned; eligibility flag explicit. |
| 28 — AI-build standard | PASS | Explicit merge rules, qty bounds, idle detection. |

**Gate**: PASS.

## Phase A — Primitives
- `Primitives/CartTokenProvider.cs` — issues + validates opaque tokens (HMAC-signed, 30-day lifetime).
- `Primitives/CartMerger.cs` — anon + auth cart merge with qty-cap awareness.
- `Primitives/EligibilityEvaluator.cs` — combines restriction + inventory + B2B gates into `checkoutEligibility`.

## Phase B — Persistence
- 5 tables: `carts`, `cart_lines`, `cart_saved_items`, `cart_b2b_metadata`, `cart_abandoned_emissions`.
- Migration `Cart_Initial`.

## Phase C — Customer slices
- `Customer/GetCart/{Request,Handler,Endpoint}.cs` — compute everything on read.
- `Customer/AddLine/*.cs`.
- `Customer/UpdateLine/*.cs`.
- `Customer/RemoveLine/*.cs`.
- `Customer/Merge/*.cs` (triggered on login by spec 004 post-login hook).
- `Customer/ApplyCoupon/*.cs`, `RemoveCoupon/*.cs`.
- `Customer/MoveToSaved/*.cs`, `MoveFromSaved/*.cs`.
- `Customer/SetB2BMetadata/*.cs`.
- `Customer/SwitchMarket/*.cs` (archives old cart).
- `Customer/RestoreCart/*.cs`.

## Phase D — Admin slices
- `Admin/GetCart/*.cs` (support read-only, audit-logged).
- `Admin/ListAbandonedCarts/*.cs` (for support follow-up).

## Phase E — Workers
- `AbandonedCartWorker` — 10 min tick; emits `cart.abandoned` for 60-min idle carts with email; dedupes via `cart_abandoned_emissions`.
- `GuestCartCleanupWorker` — daily; purges carts with `status=anonymous AND updated_at < now - 30d`.
- `ArchivedCartReaperWorker` — daily; purges archived carts older than 7d.

## Phase F — Integration
- Hook into spec 004 login success → call `CartMerger`.
- Hook into spec 005 `catalog.product.archived` event → flag affected cart lines.
- Hook into spec 008 `product.availability.changed` event → cache-bust cart-read availability.

## Phase G — Testing
- Unit: merger (100 scenarios), eligibility evaluator, token provider.
- Integration (Testcontainers): full merge, reservation lifecycle, abandonment dedup.
- Contract: each FR.

## Phase H — Polish
- AR editorial pass on `cart.ar.icu`.
- OpenAPI regen.
- Fingerprint + DoD.

## Complexity tracking
| Item | Why | Mitigation |
|---|---|---|
| Preview pricing on every read | Principle 10 correctness (no stale stored totals). | spec 007-a p95 ≤ 40 ms; cart read p95 budget 120 ms holds. |
| Market-scoped single cart | Simplifies state, avoids cross-market confusion. | Archive-and-restore path covers user mistakes. |
| Inventory reservation at add | Principle 11 operational realism. | spec 008 concurrency is already SC-tested. |

**Post-design re-check**: PASS.
