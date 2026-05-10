# Research: 026 — Shipping

**Phase**: 0
**Date**: 2026-05-10

## §1 — Zone resolution strategy
**Decision**: Composite (city-list + postal-code prefix regex). City-list is the primary match; postal-code prefix is the tie-breaker for cities with multiple postal regions. Polygon-based geocoding deferred to 1.5 — overkill for launch zone count (~12 zones across both markets).
**Rationale**: KSA + EG postal infrastructure has variable coverage; city-list-first matches most addresses correctly; postal-code regex handles ambiguity.
**Alternatives**: Pure postal-code regex (rejected — too brittle), pure polygon (rejected — geocoding cost + accuracy unstable in EG).

## §2 — Label-PDF storage pattern
**Decision**: Azure Blob Storage container `shipping-labels-<env>` per environment; SAS-signed URL on read with 5-minute TTL; blob tier `Hot` for 90 days then `Cool` then deleted at 180 days (BR-13 says 90 days hot but accounting-reference allows up to 180 in cool tier).
**Rationale**: Blob storage is the cheapest way to retain label PDFs; SAS URLs avoid serving labels through the backend; lifecycle policy enforces retention without code.
**Alternatives**: Postgres `bytea` (too heavy), filesystem volume (not portable across ACA replicas).

## §3 — Webhook signature handling per provider
**Decision**: Per-provider HMAC-SHA256 verification using a shared secret stored in KV; raw body signature recomputed; constant-time comparison. SMSA, Aramex, and Bosta all support shared-secret HMAC.
**Rationale**: Industry-standard webhook auth; reuses 025's signature-validation pattern.
**Alternatives**: mTLS (provider support inconsistent in MENA), IP allowlists (brittle as providers move infrastructure).

## §4 — Fee-table tier-overlap detection
**Decision**: A check constraint at the DB level via a Postgres exclusion constraint on `(method_version_id, zone_id, weight_min_kg, weight_max_kg)` with operator `&&` over the weight-range. Application-level validator catches before the DB does.
**Rationale**: DB-level constraint is the only reliable way to prevent simultaneous-edit races from creating overlapping tiers.
**Alternatives**: App-only validation (race-prone), trigger-based (more complex than exclusion constraint).

## §5 — Address validation
**Decision**: **Defer to Phase 1.5**. v1 relies on provider-side address-rejection during label creation; admin handles failures via the dead-letter-label queue.
**Rationale**: Address-validation services (Google, Mapbox) carry per-call cost + add another vendor; provider-side rejection is sufficient for launch volume; KSA/EG addressing is messy enough that a generic validator would create false-positives.
**Alternatives**: Google Places API (cost), bespoke regex (high false-positive rate in AR addresses).

## §6 — Multi-package deferral
**Decision**: v1 enforces single-package per order at the DB level (one `shipments` row per `order_id`). Multi-package + multi-warehouse deferred to Phase 2 (multi-vendor work would require it anyway).
**Rationale**: Single-vendor launch + simple SKU set; multi-package adds material UX + state-machine complexity not justified for launch volume.

---

All six resolutions decided. No NEEDS CLARIFICATION remaining.
