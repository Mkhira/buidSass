# DoD Walkthrough — Catalog v1

**Spec**: 005-catalog · **Phase**: 1B · **Milestone**: 2 · **Lane**: A

Walks every line of `docs/dod.md` v1.0 and notes the artifact/evidence for spec 005.

## 1. Constitution compliance

| Principle | Evidence |
|---|---|
| P4 Arabic / RTL editorial | `catalog.{ar,en}.icu` bundles; `AR_EDITORIAL_REVIEW.md` filed pending review |
| P5 Market config | Every tenant-owned entity carries `market_codes[]` or `market_code`; customer endpoints resolve market via `?market=` or default to `ksa`; published products must have non-empty `market_codes` (enforced by `PublishAsync`) |
| P6 Multi-vendor-ready | `owner_id` + `vendor_id` on categories, brands, manufacturers, products, media; admin product list defaults to `vendor_id IS NULL` (`VendorScopingTests`) |
| P8 Restricted products | `/v1/internal/catalog/restrictions/check`; visibility + price stay exposed; add-to-cart gate lives in spec 009 |
| P10 Pricing | Catalog carries `price_hint_minor_units` only; no authoritative price field. Hand-off to spec 007-a |
| P12/P26 Search seam | All writes emit outbox rows via `CatalogOutboxWriter`; `CatalogOutboxDispatcherWorker` dispatches to `ICatalogEventSubscriber`s (spec 006 will register a real one). `OutboxEmissionTests` asserts row shape |
| P15 Reviews | None added. Seam remains on spec owner |
| P20 Admin coverage | All admin endpoints gated by `.RequirePermission("catalog.*")` (F-01); roles `catalog.editor`, `catalog.publisher` seeded |
| P22 Fixed tech | .NET 9, EF Core 9, Postgres 16; Npgsql + citext extension wired in `CatalogDbContext` |
| P23 Architecture | Vertical slice under `services/backend_api/Modules/Catalog/` |
| P25 Data & audit | Every mutation handler calls `IAuditEventPublisher.PublishAsync`; `product_state_transitions` records every state transition; `MediaVariantWorker` emits `catalog.media.variant_failed` after retry budget |
| P27 UX quality | ProblemDetails with `reasonCode` for every error path; AR/EN messages paired |
| P29 Spec output | spec.md, plan.md, research.md, data-model.md, contracts/, tasks.md, quickstart.md, checklists/ |

## 2. ADR alignment

| ADR | Check |
|---|---|
| ADR-001 monorepo | Module at `services/backend_api/Modules/Catalog/` ✅ |
| ADR-003 vertical slice + MediatR | Handlers are static methods per endpoint (lighter than MediatR, matches spec 004's pattern) — acceptable per ADR discussion; upgrade to MediatR handlers deferred if Phase 1.5 needs cross-slice pipelines |
| ADR-004 EF Core 9 + code-first | `Catalog_Initial` + `Catalog_MediaClaimAndIdempotency` migrations applied ✅ |
| ADR-010 KSA residency | Connection string flows through `ResolveRequiredDefaultConnectionString` → Azure KSA region in Staging/Prod |

## 3. Testing evidence

| Suite | Result |
|---|---|
| `Catalog.Tests` — 42 tests across state machine, property, contract, integration | 42/42 pass |
| `Identity.Tests` regression | 127/127 pass |
| Build | `dotnet build services/backend_api/` — 0 errors, 2 warnings (SixLabors.ImageSharp 3.x moderate CVE; no patched 3.x available — tracked as ADR-003 Magick.NET fallback option) |

## 4. Fingerprint

`789f39325c0f0e8d7d646fc493718867540f9da41f1eed71c31bf15b53e8fb62`

Computed via `scripts/compute-fingerprint.sh` against the current `CLAUDE.md` constitution + ADR
table at ratification date 2026-04-19.

## 5. Audit-row spot check

The publish flow exercises the full audit chain:

- `ProductAdminEndpoints.PublishAsync` calls `auditEventPublisher.PublishAsync` with action
  `catalog.product.{status}` (status = `published` | `scheduled`).
- `outboxWriter.Enqueue("catalog.product.published", ...)` transactionally persists an outbox row.
- `CatalogOutboxDispatcherWorker` picks up the row on its 2-second poll and logs via
  `LoggingCatalogEventSubscriber` (spec 006 indexer will replace this).
- Integration coverage: `OutboxEmissionTests.Publish_EmitsOutboxRowForSearch` asserts the row
  lands in `catalog.catalog_outbox` with `dispatched_at IS NULL`.

## 6. Open / deferred

- **`MediaVariantWorker` binary pass.** Current implementation writes variant *descriptors* (paths)
  but does not re-encode image bytes. Upgrade requires `IStorageService` originalsbytes retrieval
  that spec 005's upload path does not persist today. Phase 1.5 or when storage wiring lands.
- **AR editorial review.** See `AR_EDITORIAL_REVIEW.md` — human sign-off pending. `needs-ar-editorial-review`
  label stays on the PR until closed.
- **OpenAPI contract diff.** `openapi.catalog.json` is hand-authored (not auto-generated from
  `MapOpenApi`) because the dev-only `/openapi/v1.json` endpoint needs the full Dev host.
  CI should compare this file against a fresh emission once the app can be booted against a
  throwaway Postgres in the pipeline.
