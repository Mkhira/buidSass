# Events Contract: Search (006)

**Date**: 2026-04-20 | **Spec**: [spec.md](./spec.md)

The search module is both a **consumer** (of catalog domain events → incremental reindex) and a **publisher** (audit events for admin actions and job lifecycle). No customer-facing domain events are emitted.

---

## 1. Consumed Events (from spec 005 catalog)

All consumed via MediatR `INotificationHandler<T>` under `Features/Search/Indexing/Subscribers/`.

| Event | Action taken | Target fields |
|---|---|---|
| `ProductPublished` | Full document upsert (build doc from catalog read model) | all |
| `ProductUpdated` | Partial upsert of changed fields (fall back to full upsert if schema_version mismatch) | varies |
| `ProductUnpublished` | Document delete | — |
| `ProductArchived` | Document delete | — |
| `VariantPublished` / `VariantUpdated` / `VariantArchived` | Re-derive parent product's `skus` + `barcodes` + `availability_state` → partial upsert | `skus`, `barcodes`, `availability_state` |
| `CategoryRenamed` / `CategoryMoved` | Fan-out re-upsert for all products in affected subtree (streamed) | `category_ids`, `category_names_ar/en` |
| `BrandRenamed` | Fan-out re-upsert for brand's products | `brand_name_ar/en` |

Each handler:
- Enqueues a `ReindexCommand` onto the in-process channel (R5).
- Returns immediately — no blocking on provider call.
- Carries `correlation_id` from the upstream event for trace continuity.

**Propagation SLA**: p95 ≤ 2s from event receipt to index visibility (SC-001).

---

## 2. Published Audit Events (→ `audit_events` table, spec 003)

All audit events use `actor_subject_id` (from spec 004), include `market_code`, `correlation_id`, and a structured `details` JSON blob. No PII in `details`.

| Action code | Emitted when | Details payload keys |
|---|---|---|
| `search.reindex.requested` | Admin hits `POST /admin/search/reindex` | `jobId`, `scope`, `subsetFilter?` |
| `search.reindex.started` | Worker picks up queued job | `jobId`, `totalProducts` |
| `search.reindex.succeeded` | Worker drains job with zero failures | `jobId`, `processedProducts`, `durationMs` |
| `search.reindex.failed` | Worker exhausts retries or encounters fatal error | `jobId`, `processedProducts`, `failedProducts`, `errorSummary` |
| `search.reindex.cancelled` | Admin (or supervisor) cancels | `jobId`, `reason` |
| `search.dead_letter.replayed` | Admin replays via `/admin/search/dead-letter/{id}/replay` | `deadLetterId`, `productId`, `commandType` |
| `search.dead_letter.resolved` | Worker successfully processes replay | `deadLetterId`, `productId` |
| `search.schema.bootstrapped` | `EnsureIndexAsync` creates or upgrades index | `marketCode`, `schemaVersion` |

---

## 3. Internal Signals (not audited, logged only)

Emitted to the observability pipeline (Serilog + OpenTelemetry) under structured event names. These are **not** persisted as audit events — they are for SRE visibility only.

| Signal | Fields (see R11) |
|---|---|
| `search.query.executed` | `query_hash`, `query_len`, `lang`, `market`, `hit_count`, `latency_ms`, `clamp_flags`, `caller_hash`, `provider`, `empty_state_reason?` |
| `search.autocomplete.executed` | same base, plus `product_hits`, `brand_hits`, `category_hits` |
| `search.reindex.command_enqueued` | `market`, `command_type`, `queue_depth` |
| `search.reindex.command_failed` | `market`, `command_type`, `product_id`, `attempt`, `error_class` |
| `search.provider.health_probe` | `market`, `status`, `latency_ms`, `doc_count` |

---

## 4. FR / SC Traceability

| Source | Events satisfying it |
|---|---|
| FR-013 (incremental reindex) | All consumed events in §1 |
| FR-014 (full reindex) | `search.reindex.requested` → `started` → `succeeded`/`failed` |
| FR-016 (retry + dead-letter) | `search.reindex.command_failed` (log), `search.dead_letter.replayed` (audit) |
| FR-021 (query observability) | `search.query.executed`, `search.autocomplete.executed` |
| FR-024 (admin audit) | All `search.reindex.*` and `search.dead_letter.*` audit rows |
| SC-001 (propagation ≤ 2s) | Measured between catalog event timestamp and `search.query.executed` hit visibility |
| SC-007 (full reindex ≤ 5 min) | Measured via `search.reindex.succeeded.durationMs` |

---

## 5. Non-Emitted (explicitly deferred)

- `search.hit.clicked` — deferred to spec 028 (click tracking).
- `search.result.zero_intent_recovered` — deferred (requires click tracking to close the loop).
- `search.synonym.edited` — deferred to spec 1.5-d (admin console).
