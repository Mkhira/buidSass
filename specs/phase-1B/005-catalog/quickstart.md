# Quickstart — Catalog v1 (Spec 005)

## Prerequisites
- Branch `phase-1B-specs` checked out.
- A1 env up (`scripts/dev/up`): Postgres 16, MinIO, Meilisearch container idle.
- Spec 004 module merged (this spec depends on admin JWT + RBAC).

## 30-minute walk-through

1. **Read spec + plan.** 27 FRs, 10 SCs, 6 user stories.
2. **Lay down Primitives.** CategoryTree closure-table helper, ProductStateMachine, AttributeSchemaValidator, MediaVariantGenerator (ImageSharp), RestrictionEvaluator, RestrictionCache, outbox primitives.
3. **Persistence.** 12 entities, EF configs, migration `Catalog_Initial`, soft-delete filters. Apply against A1 Postgres.
4. **Reference data seeder.** Compile YAML category-attribute-schemas → `category_attribute_schemas`. Seed brands/manufacturers for Dev via `CatalogDevDataSeeder`.
5. **Admin write slices.** Categories → brands → products → media → documents. Each slice is `Request/Validator/Handler/Endpoint` (ADR-003).
6. **Workflow slices.** Submit/Publish/Schedule/CancelSchedule/Archive with ProductStateMachine enforcement.
7. **Customer read slices.** ListCategories, CategoryProducts (with facet counts), ProductBySlug.
8. **Restriction API.** `/v1/internal/catalog/restrictions/check` + 5 s cache.
9. **Workers.** ScheduledPublishWorker (60 s tick), MediaVariantWorker (queue consumer), CatalogOutboxDispatcherWorker (polls `catalog_outbox` every 2 s).
10. **Bulk import.** JSON-Lines streaming, per-row idempotency.
11. **AR/EN bundles + editorial pass.**
12. **Tests.** Unit (state machine, closure invariants, schema validator). Integration (WebAppFactory + Testcontainers). Contract (one per Acceptance Scenario). Golden-file for DTOs.

## DoD
- [ ] 27 FRs → ≥ 1 contract test each.
- [ ] 10 SCs → measurable check.
- [ ] ProductStateMachine enumerated per Principle 24.
- [ ] AR/EN parity test green.
- [ ] No draft/review/archived products resolvable by slug on customer surface.
- [ ] Fingerprint + constitution check on PR.
- [ ] Outbox dispatch smoke: publish a product → spec 006 mock consumer sees event within 5 s.
