# Implementation Plan: Inventory (008)

**Branch**: `phase_1B_creating_specs` (spec branch) | **Date**: 2026-04-20 | **Spec**: [spec.md](./spec.md)

## Summary

Deliver Phase 1B inventory as a vertical slice at `services/backend_api/Features/Inventory/`. Append-only `stock_movements` ledger backed by a denormalised `variant_stock_snapshot` table with a monotonic `ats_version` for compare-and-swap concurrency. Reservation state machine (`soft_held → committed → fulfilled | released | expired`) with TTL expiry via a background scanner. Per-batch tracking with FEFO picking at commit time. Daily batch-expiry sweep runs at 03:00 UTC but evaluates expiry against warehouse-local midnight (Asia/Riyadh, Africa/Cairo). Domain events emitted via MediatR `INotification` for consumption by search (006), notifications (025), and orders (011). Multi-warehouse-ready data model with a single default warehouse seeded per market at launch.

**Technical approach**: .NET 9 + MediatR (ADR-003) with per-endpoint handlers. EF Core 9 (ADR-004) with Postgres `xmin` for row version on authored tables and a dedicated `ats_version bigint` column on the snapshot table for optimistic CAS on reservation writes. Reservations, movements, and snapshot updates commit in a single DB transaction. Reservation expiry scanner + batch expiry sweeper both implemented as `IHostedService` workers with cron schedules (NodaTime). Property-based tests (FsCheck) drive ledger-replay correctness (SC-004, SC-010) and concurrency (SC-001). Load tests (k6) enforce SC-002/SC-003. Events published through the same MediatR pipeline used by catalog (005) and pricing (007).

## Technical Context

**Language/Version**: C# 13 / .NET 9
**Primary Dependencies**: ASP.NET Core 9, MediatR, FluentValidation, EF Core 9 (Npgsql), NodaTime (time-zone-aware expiry), FsCheck.Xunit, Serilog + OpenTelemetry, Hangfire-free `IHostedService` workers + `BackgroundService`
**Storage**: PostgreSQL in Azure Saudi Arabia Central; schema `inventory`
**Testing**: xUnit + FluentAssertions; FsCheck property tests; WebApplicationFactory + Testcontainers Postgres integration; k6 perf
**Target Platform**: Linux container, .NET 9
**Project Type**: web-service
**Performance Goals**: Availability batch p95 ≤ 120 ms; reservation create p95 ≤ 250 ms at 20 lines; 0 over-allocation under 100 k concurrent attempts; expiry scanner p99 ≤ 60 s lag
**Constraints**: Single-region (ADR-010); per-market warehouse seeded once; max 100 k abs(quantity) per single movement; reservation TTL 60–3600 s; preorder floor −1000 per (variant, warehouse)
**Scale/Scope**: 10 k variants × 2 markets × 1 default warehouse = 20 k snapshot rows at launch; expected peak reservation rate ~30/s during promo events; ledger growth ~200 k rows/month

## Constitution Check

| Principle / ADR | Gate | Status |
|---|---|---|
| 11 — Inventory depth | Ledger + ATS + reservation TTL + batch/lot/expiry + FEFO + low-stock all first-class | PASS |
| 21 — Operational readiness | Admin adjustment, force-release, cycle-count, ledger export, low-stock + expiry dashboards | PASS |
| 24 — State models | Reservation + Batch state machines in §6.2 | PASS |
| 25 — Audit | §3.9: admin writes + reservation transitions audited | PASS |
| 27 — UX states | §5 covers in_stock/low/out/preorder, admin views, error states | PASS |
| 28 — AI-build standard | Vertical-slice per handler; explicit FRs + SCs | PASS |
| 29 — Required output | All 12 template sections populated in spec.md | PASS |
| ADR-003 | MediatR vertical slice | PASS |
| ADR-004 | EF Core 9 code-first + xmin concurrency + CAS via ats_version | PASS |
| ADR-010 | Postgres in Azure SA Central; per-market logical partitioning via warehouse + market_code | PASS |

**No violations. No complexity-tracking entries.**

## Project Structure

```text
specs/phase-1B/008-inventory/
├── plan.md
├── spec.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── inventory.openapi.yaml
│   └── events.md
├── checklists/requirements.md
└── tasks.md
```

```text
services/backend_api/Features/Inventory/
├── Availability/         # GET /inventory/availability, POST /validate
├── Reservations/         # create/extend/commit/fulfil/release handlers + state machine
├── Movements/            # return-restock + admin adjustment + transfer handlers
├── Batches/              # batch CRUD + FEFO picker + expiry sweeper
├── Warehouses/           # seeds + read endpoint
├── Thresholds/           # low-stock threshold read/update
├── Snapshots/            # VariantStockSnapshot read path + CAS writer
├── Workers/              # ReservationExpiryScanner, BatchExpirySweeper hosted services
├── Events/               # MediatR notification definitions
├── Persistence/          # InventoryDbContext, EF configs
├── Observability/        # structured logger, audit emitter
└── Shared/               # DTOs, error codes, reason-code seeds
services/backend_api/Tests/
├── Inventory.Unit/
├── Inventory.Properties/   # FsCheck — replay, CAS, FEFO
├── Inventory.Integration/  # Testcontainers Postgres + concurrency
└── Inventory.Contract/
```
