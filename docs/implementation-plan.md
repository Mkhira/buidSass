# Implementation Plan — Dental Commerce Platform

**Status**: **Approved v1.0**
**Authority**: Subordinate to `.specify/memory/constitution.md` (v1.0.0, 2026-04-19)
**Supersedes**: the ChatGPT "AI-Build Execution Plan"
**Owner**: Mohamed Khira
**Approved**: 2026-04-19
**Last updated**: 2026-04-19
**Program start**: floats — Week 1 counted from approval of spec `001-governance-and-setup`
**Launch target**: Week 25 (relative) — see Section 10 Gantt
**Execution model**: AI-agent-led (Claude / Codex = Lane A; GLM = Lane B) under every-PR human review

---

_Last updated 2026-04-20. See Changelog at end._

## Table of Contents

1. [Executive summary](#1-executive-summary)
2. [How this doc fits the workspace](#2-how-this-doc-fits-the-workspace)
3. [Delivery philosophy](#3-delivery-philosophy)
4. [Phase model](#4-phase-model)
5. [Workstream layout (AI-agent-led)](#5-workstream-layout-ai-agent-led)
6. [Tech decisions locked by the constitution](#6-tech-decisions-locked-by-the-constitution)
7. [Architecture decisions (ADRs)](#7-architecture-decisions-adrs)
8. [Decomposition into Spec-Kit feature specs](#8-decomposition-into-spec-kit-feature-specs)
9. [Stage-by-stage plan](#9-stage-by-stage-plan)
10. [Milestones](#10-milestones)
11. [Definition of Done](#11-definition-of-done)
12. [Risk register](#12-risk-register)
13. [Launch-readiness checklist](#13-launch-readiness-checklist)
14. [Next actions](#14-next-actions)
15. [Appendix — disposition of the original ChatGPT plan](#15-appendix--disposition-of-the-original-chatgpt-plan)

---

## 1. Executive summary

A bilingual (Arabic + English) dental commerce platform for Egypt and Saudi Arabia targeting dentists, clinics, dental labs, students, and general consumers. The stack is locked: Flutter for customer mobile and web storefront, .NET for backend, PostgreSQL for the database, a separate Next.js admin web app. Launch is single-vendor but the architecture must remain multi-vendor-ready. V1 is not a demo — it ships with real B2B flows, verification, restricted-product eligibility, tax invoices, inventory depth, and an operationally complete admin.

This plan converts those constraints into a concrete delivery sequence executed by AI coding agents (Claude + Codex on Lane A, GLM on Lane B) under every-PR human review, structured as ten stages, nine milestones, and 29 feature specs managed via Spec-Kit. Every scope item carries an explicit phase label (1 / 1.5 / 2). Arabic and RTL are gates at every milestone exit, not a late-stage QA pass. Data residency for KSA PDPL is locked to Azure Saudi Arabia Central before any production provisioning.

---

## 2. How this doc fits the workspace

- **Constitution** (`.specify/memory/constitution.md`) is non-negotiable. If this plan contradicts it, the constitution wins and this plan must be revised (Principle 31).
- This is a **program-level roadmap**, not a feature spec. Feature-level work is authored through `/speckit-specify`, `/speckit-clarify`, `/speckit-plan`, `/speckit-tasks`, `/speckit-implement` into `specs/###-feature-name/` folders (numbered by `.specify/scripts/bash/create-new-feature.sh`).
- Git hooks in `.specify/extensions.yml` auto-commit after each `/speckit-*` step. Use those instead of ad-hoc commits to keep spec history coherent.
- No code is produced at the roadmap level. The roadmap orchestrates sequence, dependencies, and gates.
- **Cross-cutting concerns with an explicit home**: audit-log module → spec `003-shared-foundations`; CI/CD bootstrap + testing strategy → spec `002-architecture-and-contracts`; ERD / data model overview → spec `002-architecture-and-contracts`; agent-context injection pattern + CODEOWNERS → spec `001-governance-and-setup`. These are named here to prevent them from drifting into "everywhere and nowhere".

---

## 3. Delivery philosophy

**Spec → contract → backend logic → UI → integrations → QA.**

Do not let UI implementation invent business logic. Business rules, states, contracts, and validations must be defined before any code touches them.

Corollary rules:

- State-model authoring is a blocking exit gate for any Principle-24 domain (verification, cart/checkout, payment, order, shipment, return/refund, quote) before code in that domain begins.
- Arabic/RTL is built into every milestone — never retrofitted.
- Every module must be reviewed for multi-vendor-ready assumptions (no hardcoded single-vendor logic in ownership, payout, commission, role structure, inventory ownership, or fulfillment ownership).
- No `Proposed`-state ADR may be coded against in production.
- Every agent session starts cold — the constitution + ADR table must be injected into the session prompt before any spec work begins (Guardrail #3, Section 5.1).

---

## 4. Phase model

Constitution Principle 30 defines three program phases: **Phase 1** (launch), **Phase 1.5** (operate), **Phase 2** (expand). To make Phase 1 executable as discrete spec-kit batches, it is split into **six sub-phases (1A–1F)**. Sub-phases are an execution grouping only — they do not amend the constitution's phase model.

| Phase | Intent | Where in this plan |
|------|--------|--------------------|
| **Phase 1A** · Foundation | Governance, architecture, shared scaffolding, **three-environment runtime (Dev/Staging/Prod), Docker + local compose, seed framework** | Milestone 1 · specs 001–003 + A1 (envs/Docker/seed) |
| **Phase 1B** · Core Commerce | Identity through returns — every transaction-critical backend domain | Milestones 2–4 · specs 004–013 |
| **Phase 1C** · Customer & Admin UI | Flutter customer shell + Next.js admin shell and CRUD | Milestones 5–6 · specs 014–019 |
| **Phase 1D** · Business Modules | Verification, B2B, promotions UX, reviews, support, CMS | Milestone 7 · specs 007-b, 020–024 |
| **Phase 1E** · Integrations | Notifications, shipping, payments providers | Milestone 8 · specs 025–027 |
| **Phase 1F** · Launch Hardening | QA, localization audit, security, performance, launch checklist | Milestone 9 · spec 029 |
| **Phase 1.5** · Operate | Analytics depth, WhatsApp, save-for-later, recommenders, synonym console, preference UI, B2B reorder | Post-launch · 7 items |
| **Phase 2** · Marketplace | Vendor portal, commissions, payouts, split checkout | Out of scope here |

Sub-phase gate rule: **a sub-phase may only begin once every spec of the prior sub-phase is at DoD**. Exception: Phase 1C UI work on a given domain MAY begin the moment that domain's Phase 1B contract is merged (per Lane A → Lane B handoff in §5).

```
 Phase 1 (launch-blocking, Week 1..25)
 +-----------+  +-----------+  +-----------+  +-----------+  +-----------+  +-----------+
 |   1A      |->|   1B      |->|   1C      |->|   1D      |->|   1E      |->|   1F      |
 | Foundation|  | Core      |  | Customer+ |  | Business  |  | Integr'ns |  | Launch    |
 | 001-003+A1|  | Commerce  |  | Admin UI  |  | Modules   |  | 025-027   |  | Hardening |
 |  3 specs  |  | 004-013   |  | 014-019   |  | 007-b,    |  |  3 specs  |  |    029    |
 |           |  |  10 specs |  |  6 specs  |  | 020-024   |  |           |  |  1 spec   |
 |           |  |           |  |           |  |  6 specs  |  |           |  |           |
 +-----------+  +-----------+  +-----------+  +-----------+  +-----------+  +-----------+

       |                                                                        |
       v                                                                        v
 Phase 1.5 (operate, post-launch)                                    Phase 2 (expand)
 +------------------------------------+                              +-------------------+
 | 028 analytics, 1.5-a..1.5-f        |                              | Marketplace /     |
 | (save-for-later, recs, synonyms,   |                              | vendor / payouts  |
 |  pref-ui, whatsapp, b2b-reorder)   |                              | (out of scope)    |
 +------------------------------------+                              +-------------------+
```

### 4.1 Phase scope inventory (spec-ready)

Every item below is a self-contained input for `/speckit-specify`. Copy the **Spec title** verbatim into the command; the **One-liner** is a starter description the spec command will expand. Phase labels map 1:1 to the constitution's Principle 30.

#### How to run a spec task (GitHub Spec Kit cadence)

Each task below is a runnable batch for the Spec Kit command chain. Execute in listed order within a sub-phase. Dependencies (`depends-on`) are hard gates — do not start a spec until every listed dependency is at **DoD** (Section 11).

```
./.specify/scripts/bash/create-new-feature.sh "<spec-title>"    # creates specs/NNN-<spec-title>/
/speckit-specify   "<one-liner>"                                 # authors spec.md
/speckit-clarify                                                 # resolves open questions
/speckit-plan                                                    # produces plan.md + contracts
/speckit-tasks                                                   # breaks plan into tasks.md
/speckit-implement                                               # executes tasks (Lane A or Lane B per §5)
```

Every spec MUST carry the Constitution + ADR context in its session (Guardrail #3) and pass the lint + contract bar (Guardrails #1, #2) before merge.

---

#### Phase 1A — Foundation · Milestone 1 · 3 specs + A1

**Intent**: lock repo layout, CI, agent-context injection, contracts skeleton, ERD, state machines, audit-log, storage/PDF abstractions, **plus the three-environment runtime (Development / Staging / Production), containerization, and seed framework**. **Nothing else starts until 1A is at DoD.**

Phase 1A contains **two parallel streams**:

- **Spec stream** · 001–003 authored through Spec-Kit; each gets its own spec folder.
- **Infra stream** · A1 (`environments-and-containers` + seed framework) — platform scaffolding that underpins every later spec; no per-spec clarify/plan cadence required because it is a fixed, one-shot retrofit governed by `docs/missing-env-docker-plan.md`.

Both streams MUST be at DoD before Phase 1B begins.

##### 001 · `governance-and-setup`

- **depends-on**: none
- **exit**: CI green on empty repo; CODEOWNERS blocks constitution edits; `CLAUDE.md` injects Constitution + ADR table; every-PR review active
- **tasks**:
  1. Scaffold monorepo per ADR-001 (`apps/customer_flutter`, `apps/admin_web`, `services/backend_api`, `packages/shared_contracts`, `packages/design_system`, `infra/`, `scripts/`).
  2. Add `.editorconfig` + dotnet/dart/eslint/prettier configs; wire lint+format job (Guardrail #1).
  3. Author `CLAUDE.md`, Codex rules, GLM rules that inject Constitution + ADR Decisions table (Guardrail #3).
  4. Add `CODEOWNERS` requiring human approval for `.specify/memory/constitution.md` and Section 7 ADR block (Guardrail #4).
  5. Configure branch protection: required PR review, green CI, signed commits.
  6. Define DoD checklist template; link from pull-request template.
  7. Commit per-spec session-init script that pastes Constitution + ADR context on spec creation.

##### 002 · `architecture-and-contracts`

- **depends-on**: 001
- **exit**: ERD published; 7 state machines diagrammed; API style scaffolded; OpenAPI emit job green
- **tasks**:
  1. Draft full ERD covering all Phase-1 domains; commit as `docs/erd.md` + dbdiagram source.
  2. Define API style: vertical slice + MediatR (ADR-003) — folder conventions, request/response envelopes, error model.
  3. Diagram seven Principle-24 state machines (verification, cart, payment, order, shipment, return, quote) with states, transitions, actors, triggers.
  4. Author permissions matrix (role × resource × action) covering customer, admin roles, B2B buyer/approver.
  5. Testing strategy: unit (xUnit), integration (Testcontainers+Postgres), contract (schemathesis vs OpenAPI), E2E (Playwright + Flutter integration_test).
  6. CI/CD bootstrap: build → test → OpenAPI emit → contract diff → artifacts (Guardrail #2).
  7. Finalize any residual ADR questions; keep Section 7 in sync.

##### 003 · `shared-foundations`

- **depends-on**: 002
- **exit**: `packages/shared_contracts` published; design-token package consumed; audit-log module emits one test event; PDF + storage abstractions return signed URLs
- **tasks**:
  1. Generate `packages/shared_contracts` from OpenAPI; publish to internal feed; wire Flutter + Next.js consumers.
  2. Build `packages/design_system`: color tokens (Principle 7), typography, spacing, RTL mirroring rules, semantic colors.
  3. Localization scaffolding: ICU message format, AR + EN resource files, RTL layout helpers, editorial-review flag.
  4. Central audit-log module: domain-event subscriber, append-only table, actor + reason + before/after capture.
  5. Storage abstraction: upload, signed URL issuance, virus-scan hook, market-aware bucket routing.
  6. PDF abstraction: template engine, AR + EN layouts, RTL rendering, font embedding, tax-invoice stub template.
  7. Health-check endpoint wired; structured logging baseline; correlation-id middleware.

##### A1 · `environments-and-containers` · **shipped 2026-04-20**

- **depends-on**: 001 (repo layout, CI primitives)
- **status**: merged via PR #18; follow-up finalization in this PR
- **exit**: `scripts/dev/up.sh` brings full local stack healthy in <90 s warm; `/health` returns 200; `ASPNETCORE_ENVIRONMENT=Production dotnet run -- seed --mode=apply` exits 1 with SeedGuard message; `docker-build.yml` pushes image to GHCR; `seed-pii-guard` CI job green
- **what A1 delivers (first-class phase content, not an appendix)**:
  1. **Three-environment runtime.** `Development` (local compose, verbose logs, fake integrations), `Staging` (Azure Container Apps + KSA Central, sandbox integrations, synthetic data, 100% trace sampling), `Production` (real integrations, tiered sampling, seeding hard-blocked). Selected via `ASPNETCORE_ENVIRONMENT`; all environment deltas live in `appsettings.{env}.json` + Key Vault — never in code.
  2. **Layered configuration.** `builder.AddLayeredConfiguration()` chains json → env vars → Azure Key Vault (`DefaultAzureCredential`, Staging/Prod only). Missing KV cannot unlock seeding — `SeedGuard` + Program.cs fast-path both enforce the Production block.
  3. **Containerization.** Multi-stage `services/backend_api/Dockerfile` on `aspnet:9.0-noble-chiseled-extra` (tzdata + ICU for Arabic + Asia/Riyadh + Africa/Cairo), non-root uid 10001. `infra/local/docker-compose.yml` composes `backend_api` + `postgres:16` + `meilisearch:v1.10` + `mailhog` + OTel collector (profile-gated) with health gates and named volumes.
  4. **Dev scripts.** `scripts/dev/{up,reset,migrate,seed,logs,down}.sh` — POSIX, `set -euo pipefail`, idempotent. One-command bring-up target.
  5. **Seed framework.** `ISeeder`, `SeedRunner` (topological `DependsOn` sort + SHA256 checksum + idempotent `seed_applied` table + `seeding.applied` audit emit), `SeedGuard` (belt-and-braces Production block), `SeedingCliVerb` (`dotnet run -- seed --mode=apply|fresh|dry-run`). `--mode=dry-run` is the only mode allowed in Production (diagnostic, no writes). Per-spec seeders (identity-v1, catalog-v1, search-v1, pricing-v1, inventory-v1) ride their owning spec's PR.
  6. **CI.** `docker-build.yml` builds on every PR, pushes to GHCR on `main` (sha + `latest-main` tags). `deploy-staging.yml` is a `workflow_dispatch` placeholder — real Azure Container Apps wiring lands in Phase 1E (§ADR-010). `seed-pii-guard` regex-scans `Features/Seeding/**` for EG/KSA phone shapes, consumer email domains, and 14-digit national IDs; fails the build on any match.
  7. **Docs.** `docs/environments.md`, `docs/local-setup.md`, `docs/seed-data.md`, `docs/staging-data-policy.md`. Repo layout and DoD updated.
- **Spec-level impact (per-spec seeder tasks appended to each owning spec)**:

  | Spec | Seeder              | Notes                                                                  |
  |------|---------------------|------------------------------------------------------------------------|
  | 004  | `identity-v1`       | synthetic users per role incl. verified professionals                  |
  | 005  | `catalog-v1`        | categories, brands, products, variants, media, AR + EN                 |
  | 006  | `search-v1`         | index bootstrap against seeded catalog                                 |
  | 007  | `pricing-v1`        | coupons, bundles, tier pricing, business pricing                       |
  | 008  | `inventory-v1`      | stock, warehouses, batch/lot, expiry, reservations                     |
  | 009  | `cart-v1`           | guest + logged-in carts                                                |
  | 010  | `checkout-v1`       | in-progress sessions across every state                                |
  | 011  | `orders-v1`         | across all four orthogonal status combinations                         |
  | 012  | `tax-invoices-v1`   | EG + KSA + B2B invoice variants                                        |
  | 013  | `returns-v1`        | approve / partial / reject decisions                                   |
  | 020  | `verification-v1`   | submissions across every state                                         |
  | 021  | `quotes-b2b-v1`     | company accounts + quotes spanning all states                          |
  | 007-b| `promotions-v1`     | coupons, scheduled promos, business-pricing tiers                      |
  | 022  | `reviews-v1`        | verified-buyer reviews + moderation states                             |
  | 023  | `support-v1`        | tickets across categories, priorities, SLA states                      |
  | 024  | `cms-v1`            | banners, FAQ, legal, blog — AR + EN                                    |
  | 025  | `notifications-v1`  | templates + delivery-log entries                                       |
  | 026  | `shipping-v1`       | methods + zones + fees (EG + KSA)                                      |
  | 027  | `payments-v1`       | attempts across success / failure / retry / reconciliation-exception  |

  Each seeder MUST pass the `seed-pii-guard` CI check (no real phones, emails, or national-ID patterns) and MUST obey `docs/staging-data-policy.md`.

- **Rule (applies to every post-1A spec)**: every spec in 1B, 1D, 1E MUST (a) register any environment-specific secret via Key Vault + `AddLayeredConfiguration()` — never in `appsettings.json`, (b) ship a `<spec>-v1` seeder for Staging + local, (c) pass `seed-pii-guard`. UI specs in 1C MAY skip (b) if they add zero persistent entities.
- **Deliberately deferred to Phase 1E E1**: Azure IaC (Bicep), real Key Vault provisioning, `deploy-staging.yml` wiring, Meilisearch HA, managed Postgres Flexible Server provisioning, Flutter-web hosting decision (Static Web Apps vs container).

---

#### Phase 1B — Core Commerce · Milestones 2–4 · 10 specs

**Intent**: every transaction-critical backend domain. Lane A (Claude/Codex) lives here. Lane B MAY start the matching UI in Phase 1C the moment a spec here merges its contract.

##### 004 · `identity-and-access`

- **depends-on**: 1A
- **exit**: customer + admin auth; OTP; RBAC role framework
- **tasks**:
  1. User + role + permission tables; seed admin roles; audit on role changes.
  2. Registration + email/phone verification; password hashing (Argon2id); lockout policy.
  3. Login (email or phone) + session (JWT access + refresh) + revoke.
  4. Phone OTP service abstraction (provider decided in 1E spec 025); rate limits; replay protection.
  5. Password reset flow with single-use token.
  6. Customer vs admin separation (distinct auth surfaces).
  7. RBAC middleware; permission claim carried in token; tests cover role × resource matrix.

##### 005 · `catalog`

- **depends-on**: 004
- **exit**: product/brand/category/media/document model with restriction metadata
- **tasks**:
  1. Category tree (nested set or closure table); active/inactive.
  2. Brand + manufacturer entities.
  3. Product model: SKU, barcode, attributes (EAV or JSONB), media, documents, spec sheets.
  4. Restriction metadata (Principle 8): which products require verified professional purchase.
  5. Media pipeline via storage abstraction (003); variant generation; alt text AR + EN.
  6. Multi-vendor-ready fields (owner_id, vendor_id nullable) per Principle 6.
  7. Admin-facing + customer-facing DTOs; audit on catalog edits.

##### 006 · `search`

- **depends-on**: 005; A1 (Meilisearch local container health-gated in `infra/local/docker-compose.yml`)
- **exit**: Meilisearch index live; Arabic normalization; SKU + barcode + autocomplete
- **tasks**:
  1. Service boundary per Principle 12/26; Meilisearch adapter; swappable contract.
  2. Product indexer (initial + incremental via domain events).
  3. Arabic normalization (alef/ya/ta-marbuta folding); diacritics stripping; stopwords.
  4. Synonyms bootstrap (admin console deferred to Phase 1.5).
  5. Autocomplete + typo tolerance + SKU + barcode lookup.
  6. Facets (category, brand, price range, availability, restriction).
  7. Sort modes (relevance, price asc/desc, newness); relevance tuning.

##### 007-a · `pricing-and-tax-engine`

- **depends-on**: 005
- **exit**: price resolution pipeline; EG + KSA VAT; promotion primitives (stacking rules)
- **tasks**:
  1. Price resolution pipeline: base → business/tier → active promo → coupon → tax.
  2. VAT rules per market (KSA 15%, EG as configured); inclusive/exclusive modes.
  3. Promotion primitives: percentage, fixed, BOGO, bundle, tier; stacking + exclusion rules.
  4. Coupon model (code, usage caps, eligibility); engine only — UX is 007-b.
  5. Business + tier pricing tables; per-customer overrides.
  6. Auditable price breakdown returned with every cart/checkout total.
  7. Property-based tests for pricing correctness; golden-file tests per market.

##### 008 · `inventory`

- **depends-on**: 005
- **exit**: stock + ATS + soft-hold/TTL + hard-commit + low-stock + batch/lot/expiry
- **tasks**:
  1. Stock ledger (append-only movements); warehouses table; multi-warehouse-ready.
  2. Available-to-sell computation (on-hand − reserved − allocated).
  3. Soft-hold reservation with TTL on checkout-start.
  4. Hard-commit on payment-auth; release on cancel/timeout.
  5. Low-stock thresholds + domain-event emission for alerts.
  6. Batch/lot numbers + expiry tracking; FEFO picking guidance.
  7. Reservation-inspection API for admin; audit on adjustments.

##### 009 · `cart`

- **depends-on**: 007-a, 008
- **exit**: guest + logged-in carts; merge on login; coupon application; validation
- **tasks**:
  1. Cart model (guest via cookie/device id, logged-in via user id); line items; metadata.
  2. Merge on login policy (sum quantities, resolve conflicts, re-validate).
  3. Coupon application against 007-a engine; multi-coupon per stacking rules.
  4. Validation: stock, restrictions, quantity limits, market eligibility.
  5. Price snapshot vs re-resolve policy; explicit "prices may update at checkout" signal.
  6. Abandoned-cart marker (consumed by 025 notifications).
  7. Idempotent add/update/remove endpoints.
  8. Author `cart-v1` seeder (synthetic guest + logged-in carts for staging + local per `docs/staging-data-policy.md`).

##### 010 · `checkout`

- **depends-on**: 009
- **exit**: address, shipping, billing, payment init (stubs), restricted-product gate, stock revalidation
- **tasks**:
  1. Address book (shipping + billing) with market-specific fields.
  2. Shipping method selection + fee quote (provider stubbed; real in 1E 026).
  3. Restricted-product enforcement: block if customer not verified; surface reason.
  4. Stock revalidation + soft-hold extension at submission.
  5. Payment initiation stub with provider abstraction (real in 1E 027).
  6. Order-preview endpoint returning full price + tax breakdown.
  7. Checkout state machine (Principle 24) with retry/timeout/failure paths.
  8. Author `checkout-v1` seeder (synthetic in-progress checkout sessions covering each state).

##### 011 · `orders`

- **depends-on**: 010
- **exit**: four orthogonal status fields; history; invoice link; reorder basics
- **tasks**:
  1. Order model with **four orthogonal status fields** (order, payment, fulfillment, refund/return).
  2. Placement workflow converts checkout into order; reserves become allocations.
  3. Status history table; actor + timestamp + reason on every transition.
  4. Invoice linkage to spec 012.
  5. Reorder (clone into new cart); quote-linkage placeholder for 021.
  6. Admin + customer order views.
  7. Audit events per transition; structured log line per event.
  8. Author `orders-v1` seeder (synthetic orders across all four status combinations for staging + local).

##### 012 · `tax-and-invoices`

- **depends-on**: 011
- **exit**: EG + KSA tax invoices; AR + EN PDF; finance export views
- **tasks**:
  1. Tax-invoice entity (sequence per market, legal fields, VAT number).
  2. Template renderer via 003 PDF abstraction; AR + EN; RTL pass.
  3. KSA ZATCA-compliant fields (QR placeholder; full phase-2 compliance tracked separately).
  4. EG ETA-compliant fields per current law.
  5. B2B invoice variant (company name, VAT id, PO number).
  6. Finance export view (CSV + PDF bundle) for admin.
  7. Audit on regenerate; immutable originals.
  8. Author `tax-invoices-v1` seeder (synthetic invoices covering EG + KSA + B2B variants).

##### 013 · `returns-and-refunds`

- **depends-on**: 011
- **exit**: return submission, eligibility, admin decision, refund execution, full state model
- **tasks**:
  1. Return eligibility rules (window, condition, restricted-product restocking rules).
  2. Customer submission flow; reason codes; photo upload via storage.
  3. Admin review queue + decision (approve/partial/reject) with audit.
  4. Refund execution against original payment method (provider abstracted; real in 027).
  5. Inventory reversal on approved return.
  6. Return state machine (Principle 24).
  7. Customer-visible timeline.
  8. Author `returns-v1` seeder (synthetic returns spanning approve/partial/reject decisions).

---

#### Phase 1C — Customer & Admin UI · Milestones 5–6 · 6 specs + C-Infra

**Intent**: Lane B (GLM) consumes merged contracts from 1B. UI-only — any backend gap found here escalates back to the owning 1B spec (never inline fix).

**Phase 1C-Infra** (runs in parallel with 014/015): author `apps/admin_web/Dockerfile` (multi-stage, `node:20-alpine` runtime + Next.js `output: 'standalone'`, non-root uid 10001, matches the A1 backend recipe), add `.github/workflows/admin-docker-build.yml` pushing to GHCR on `main` (sha + `latest-main` tags), extend compose with an optional `admin_web` service behind an `admin` profile. Flutter-web hosting decision (static via Azure Static Web Apps vs container) is deferred to Phase 1E E1.

##### 014 · `customer-app-shell`

- **depends-on**: 004–013 contracts merged to `main` (not just DoD)
- **exit**: Flutter (Bloc) Android + iOS + web: shell, auth, home, listing, detail, cart, checkout, orders, more-menu; RTL + AR editorial pass
- **tasks**:
  1. App shell + routing + Bloc setup; app-wide theming via `packages/design_system`.
  2. Localization + RTL; editorial AR strings pass.
  3. Auth screens (register, login, OTP, reset); session management.
  4. Home (banners from CMS stub, featured sections, categories).
  5. Product listing (facets, sort, Arabic search) + product detail (media, specs, restricted badge, price breakdown).
  6. Cart + checkout screens wired to 009/010 contracts.
  7. Orders list + detail + reorder + support shortcut; more-menu (addresses, language, logout, verification CTA).

##### 015 · `admin-foundation`

- **depends-on**: 004 contract merged to `main`
- **exit**: Next.js + shadcn/ui shell; auth; role-based nav; AR + EN; audit-log reader
- **tasks**:
  1. Next.js app scaffold + shadcn/ui base; AR + EN i18n; RTL toggle.
  2. Admin auth; RBAC guard per route.
  3. Shell layout: sidebar, topbar, breadcrumbs, global search.
  4. Audit-log reader (filter by actor, resource, timeframe).
  5. Shared table + form components (pagination, filters, saved views).
  6. Notification center (in-app alerts for admin tasks).
  7. Accessibility pass (keyboard nav, focus rings, contrast).

##### 016 · `admin-catalog`

- **depends-on**: 005 contract merged to `main`, 015
- **exit**: CRUD for category/brand/product/media/docs; restriction metadata; bulk ops
- **tasks**:
  1. Category tree editor (drag-reorder, activate/deactivate).
  2. Brand CRUD.
  3. Product CRUD with attribute editor; AR + EN content tabs.
  4. Media + document upload via storage abstraction; variant previews.
  5. Restriction flag editor + rationale field.
  6. Bulk import/export (CSV) with validation report.
  7. Draft + publish workflow; audit on publish.

##### 017 · `admin-inventory`

- **depends-on**: 008 contract merged to `main`, 015
- **exit**: stock adjustments, low-stock queue, batch/lot, expiry, reservation inspection
- **tasks**:
  1. Stock adjustment form (reason codes, per-warehouse).
  2. Low-stock queue view; threshold editor per SKU.
  3. Batch/lot creation + linkage to receipts.
  4. Expiry calendar + near-expiry alerts.
  5. Reservation inspection (who holds what, TTL, release).
  6. Ledger view (append-only movements) with export.
  7. Audit on every adjustment.

##### 018 · `admin-orders`

- **depends-on**: 011, 013 contracts merged to `main`, 015
- **exit**: order list + detail, status transitions, refund init, invoice reprint, quote linkage
- **tasks**:
  1. Order list with filters (status × 4 fields, market, B2B flag, date range).
  2. Order detail with timeline showing all four status streams.
  3. Status-transition actions gated by state machine + permissions.
  4. Refund initiation flow (calls spec 013).
  5. Invoice reprint via spec 012.
  6. Quote linkage (from 021).
  7. Export view (CSV) for finance.

##### 019 · `admin-customers`

- **depends-on**: 004 contract merged to `main`, 015
- **exit**: profile view, verification history, quotes, support tickets, address book
- **tasks**:
  1. Customer list + filters (market, B2B, verification state).
  2. Profile detail: identity, roles, orders summary.
  3. Verification history panel (from 020).
  4. Quote history panel (from 021).
  5. Support-ticket linkage (from 023).
  6. Address book view; B2B company hierarchy view.
  7. Admin actions: suspend, unlock, trigger password reset (audited).

---

#### Phase 1D — Business Modules · Milestone 7 · 6 specs

**Intent**: layer professional + B2B + content + moderation on top of the core. Lane A and Lane B run in parallel per-spec.

##### 020 · `verification`

- **depends-on**: 004, 015
- **exit**: submission + admin review queue + approve/reject/request-info + expiry + audit
- **tasks**:
  1. Verification entity + state machine (Principle 24): submitted → in-review → approved/rejected/info-requested → expired.
  2. Customer submission: profession, license number, documents (upload via storage).
  3. Admin review queue with filters; decision actions with required reasoning.
  4. Expiry tracking + renewal reminder trigger (consumed by 025).
  5. Restricted-product eligibility hook used by 005/009/010.
  6. Market-aware fields (EG vs KSA regulator differences).
  7. Audit trail per decision.
  8. Author `verification-v1` seeder (synthetic submissions across every state for staging + local).

##### 021 · `quotes-and-b2b`

- **depends-on**: 011, 018
- **exit**: quote request → admin quote → revisions → accept → quote-to-order; company accounts; PO
- **tasks**:
  1. Company account + multi-user (buyer, approver) + branch/company hierarchy.
  2. Quote entity + state machine: requested → drafted → revised → accepted/rejected/expired.
  3. Customer quote-request flow (from cart or from product).
  4. Admin quote authoring (line-item pricing, terms, validity).
  5. Accept → convert-to-order with PO number + invoice-billing flag.
  6. Approval flow inside company accounts (buyer submits, approver accepts).
  7. Repeat-order template linkage (Phase 1.5 completes the UI, backend stubs here).
  8. Author `quotes-b2b-v1` seeder (synthetic company accounts + quotes spanning all states).

##### 007-b · `promotions-ux-and-campaigns`

- **depends-on**: 007-a, 016
- **exit**: coupon lifecycle, scheduled promos, banner-linked campaigns, business + tier pricing authoring
- **tasks**:
  1. Coupon admin UX (create, schedule, usage caps, eligibility, deactivate).
  2. Scheduled promotion authoring (start/end, target, stacking behavior).
  3. Banner-linked campaigns (CMS 024 integration for hero slots).
  4. Business-pricing authoring (per-company, per-tier).
  5. Tier-pricing table editor.
  6. Preview tool showing resolved price for a sample customer + cart.
  7. Audit on every promo/coupon/business-pricing edit.
  8. Author `promotions-v1` seeder (sample coupons, scheduled promos, business-pricing tiers).

##### 022 · `reviews-moderation`

- **depends-on**: 011, 015
- **exit**: verified-buyer enforcement; admin moderation queue; hide/delete with audit
- **tasks**:
  1. Review entity linked to delivered order line.
  2. Customer submission only if order is in delivered state + not refunded.
  3. Admin moderation queue (flag reasons, hide/delete, reinstate).
  4. Profanity / abuse filter hook.
  5. Aggregated rating on product detail.
  6. Admin notes on review (audited).
  7. Report-review flow for other customers.
  8. Author `reviews-v1` seeder (synthetic verified-buyer reviews, moderated + flagged states).

##### 023 · `support-tickets`

- **depends-on**: 011, 015
- **exit**: ticket CRUD, reply flow, category tagging, order linkage, SLA fields
- **tasks**:
  1. Ticket entity (subject, body, category, priority, linked order/return/quote).
  2. Customer ticket creation + list + detail + reply.
  3. Admin queue with filters, assignment, status transitions.
  4. SLA timers per priority; breach alerting.
  5. File attachments via storage abstraction.
  6. Internal notes (not customer-visible).
  7. Conversion between ticket and return/refund request where applicable.
  8. Author `support-v1` seeder (synthetic tickets across categories, priorities, SLA states).

##### 024 · `cms`

- **depends-on**: 015
- **exit**: banners, featured sections, FAQ, legal, blog skeleton, localized publishing
- **tasks**:
  1. Banner slots + scheduling + market/locale targeting.
  2. Featured-section composer (products, categories, bundles).
  3. FAQ entries (category, AR + EN, ordering).
  4. Legal pages (terms, privacy, returns, cookies) with version history.
  5. Blog skeleton (articles, categories, author, scheduled publish).
  6. SEO fields (meta, OG, schema.org) per entity.
  7. Preview + draft/publish flow with audit.
  8. Author `cms-v1` seeder (sample banners, FAQ, legal pages, blog posts — AR + EN).

---

#### Phase 1E — Integrations · Milestone 8 · 3 specs + E1

**Intent**: swap stubs for real providers. ADRs 007, 008, 009 get **Accepted** during this phase. Every integration must ship with reconciliation + webhook replay + idempotency tests. **E1 runs first** — it provisions the Azure runtime that 025/026/027 depend on.

##### E1 · `infrastructure-integration`

- **depends-on**: A1 (layered config + seed framework), 1A/1B/1C/1D at DoD for scope confirmation
- **exit**: Azure Container Apps environment live in KSA Central (ADR-010); Key Vault `kv-dental-stg` + `kv-dental-prd` provisioned with all ADR-007/008/009 provider secrets; `deploy-staging.yml` promotes `ghcr.io/<org>/backend-api:<sha>` to Staging ACA on every `main` merge; `apps/admin_web` container promoted the same way; Flutter-web hosting finalized (Azure Static Web Apps vs ACA container)
- **tasks**:
  1. Bicep IaC for Resource Group, Container Apps Environment, Postgres Flexible Server (managed, private endpoint), Meilisearch-hosted or self-hosted (HA) decision, Key Vaults (Staging + Production), Log Analytics workspace, App Insights.
  2. Key Vault bootstrap: register ADR-007 (payments), ADR-008 (shipping), ADR-009 (notifications) secrets under documented key names; RBAC wired to ACA managed identity.
  3. Wire `deploy-staging.yml` to authenticate via OIDC federated credential, pull GHCR image by sha, deploy `backend_api` + `admin_web` container apps, run EF migrations job, run `seed --mode=apply` (Staging only).
  4. Flutter-web hosting decision + implementation (Static Web Apps preferred for static build output; container path available if SSR-like needs emerge).
  5. Post-deploy smoke: `/health` 200, `seed --mode=dry-run` exits 0, one Meilisearch query returns results, one representative admin page renders.
  6. Runbook: rotating secrets, re-running migrations, rollback by image tag, seed-dataset refresh cadence.
  7. Alerts: deploy-failure, health-probe failure, high 5xx, Key Vault access anomalies.

##### 025 · `notifications`

- **depends-on**: 004, 011, E1
- **exit**: template mgmt; event-triggered SMS + email + push (no WhatsApp); campaign basics; preference mgmt. **ADR-009 Accepted.**
- **tasks**:
  1. Provider selection + ADR-009 flip to Accepted (SMS, email, push — WhatsApp deferred to 1.5).
  2. Template entity + AR + EN variants + placeholders; admin editor.
  3. Event subscribers for: OTP, order updates, abandoned cart, restock, price drop, verification results, refunds, shipping updates.
  4. Channel-preference management (customer-facing opt-out basics; full UI in 1.5-e).
  5. Campaign authoring (admin-triggered broadcast with targeting).
  6. Delivery logging + retry + dead-letter.
  7. Rate limits + per-market compliance (time windows, unsubscribe language).
  8. Register ADR-009 provider credentials (SMS + email + push) in Key Vault Staging + Production via E1 IaC; consume via `AddLayeredConfiguration()` — no secrets in `appsettings.json`.
  9. Author `notifications-v1` seeder (sample templates AR + EN, delivery-log entries across channels).

##### 026 · `shipping`

- **depends-on**: 010, 011, E1
- **exit**: provider settings, market rules, methods, fees, shipment state mapping, tracking webhooks. **ADR-008 Accepted.**
- **tasks**:
  1. Provider selection + ADR-008 flip to Accepted.
  2. Shipping-method entity (market, zones, fees, SLAs); admin editor.
  3. Provider adapter implementing generic shipping contract.
  4. Shipment creation on order placement; label + tracking number persisted.
  5. Tracking webhook receiver + shipment state machine (Principle 24).
  6. Fee quote endpoint used by checkout.
  7. Delivery attempt + failure + re-delivery handling.
  8. Register ADR-008 provider credentials + API keys in Key Vault Staging + Production via E1 IaC; consume via `AddLayeredConfiguration()`.
  9. Author `shipping-v1` seeder (sample methods + zones + fees covering EG + KSA markets).

##### 027 · `payments-integration`

- **depends-on**: 010, 012, E1
- **exit**: ADR-007 primary + backup per market live; BNPL (Tabby/Tamara KSA + Valu EG); reconciliation job; webhook replay. **ADR-007 Accepted.**
- **tasks**:
  1. Provider selection + ADR-007 flip to Accepted; PCI scope boundary documented.
  2. Card provider adapter (KSA + EG); Apple Pay + Mada + STC Pay for KSA; Valu for EG.
  3. BNPL adapters: Tabby + Tamara (KSA), Valu BNPL (EG).
  4. COD + bank transfer handlers.
  5. Webhook receiver + signature verification + idempotency + replay tool.
  6. Reconciliation job (daily) matching provider ledger vs internal; exception queue.
  7. Payment-retry flow for failed captures (order.payment_status transitions).
  8. Register ADR-007 provider credentials + webhook signing secrets in Key Vault Staging + Production via E1 IaC; consume via `AddLayeredConfiguration()`; PCI-scope review signed off.
  9. Author `payments-v1` seeder (sample payment attempts across success / failure / retry / reconciliation-exception states).

---

#### Phase 1F — Launch Hardening · Milestone 9 · 1 spec

**Intent**: no new features. Everything below runs against the complete system.

##### 029 · `qa-and-hardening`

- **depends-on**: all of 1A–1E at DoD
- **exit**: functional + localization + security + reliability + performance regression complete; Section 13 launch-readiness checklist 100% checked
- **tasks**:
  1. Functional regression across every user story (customer + admin + B2B).
  2. Localization audit: Arabic editorial reviewer signs off every screen, email, PDF, notification.
  3. RTL visual regression sweep.
  4. Security pass: OWASP ASVS L1, dependency scan, secret scan, auth fuzzing, IDOR checks.
  5. Reliability: chaos drills on payment, shipping, notification providers; reconciliation rerun.
  6. Performance: k6 load tests on catalog, search, checkout executed **against the Staging ACA stack at 5× expected launch RPS**; p95 budgets enforced.
  7. Production smoke: `ASPNETCORE_ENVIRONMENT=Production dotnet run -- seed --mode=dry-run` exits 0 and writes zero `seed_applied` rows; `/health` returns 200 from Production ACA.
  8. Container health verification: `backend_api`, `admin_web`, and Flutter-web (per E1 hosting decision) pass health-probes on Staging; rollback by image tag rehearsed.
  9. Launch-readiness checklist (Section 13) executed end-to-end; sign-offs captured.

Exit of 1F = **launch**. Post-launch work enters Phase 1.5.

#### Phase 1.5 — Post-launch optimization (not launch-blocking)

| # | Spec title | Phase | One-liner |
|---|------------|-------|-----------|
| 028 | analytics-audit-monitoring | 1.5 | Event tracking, conversion funnel, search analytics, dashboards, payment-failure alerts, advanced observability. (Phase 1 minimum = structured logs + basic uptime, delivered inline.) |
| 1.5-a | cart-save-for-later | 1.5 | Persist non-checkout items for later purchase; separate from active cart; surface on home + more-menu. |
| 1.5-b | home-recommendations | 1.5 | Recommender modules on customer home (recently viewed, category-based, collaborative filtering if data allows). |
| 1.5-c | b2b-reorder-templates | 1.5 | B2B reorder lists, scheduled reorders, approval workflows beyond single PO. |
| 1.5-d | search-synonyms-ops-console | 1.5 | Admin UX for managing search synonyms, stopwords, and ranking boosts. |
| 1.5-e | notifications-preference-ui | 1.5 | Customer-facing preference center beyond opt-out basics in spec 025. |
| 1.5-f | whatsapp-notifications | 1.5 | WhatsApp Business API channel in addition to SMS/email/push. Template approval + opt-in flow. Deferred from launch. |

#### Phase 2 — Marketplace / multi-vendor (explicitly out of scope)

Listed only to lock the boundary. No specs written now; every Phase 1 module's DoD requires multi-vendor-readiness so these slot in without schema rewrites.

| # | Capability | Phase | Notes |
|---|------------|-------|-------|
| 2-a | vendor-onboarding | 2 | Vendor registration, KYC, agreements, seller dashboard. |
| 2-b | vendor-catalog-ownership | 2 | Per-vendor product ownership, shared catalog rules, vendor-scoped admin. |
| 2-c | commissions-and-payouts | 2 | Commission rules, payout schedules, statements, reconciliation. |
| 2-d | split-checkout | 2 | Multi-vendor cart splitting, per-vendor shipping, consolidated payment. |
| 2-e | vendor-fulfillment | 2 | Per-vendor inventory ownership, fulfillment ownership, SLAs. |
| 2-f | marketplace-policy-engine | 2 | Listing policies, dispute handling, vendor ratings. |

> **Usage**: to author a phase item, run `./.specify/scripts/bash/create-new-feature.sh "<spec-title>"` (e.g. `create-new-feature.sh "identity-and-access"`). The script assigns the next sequential number; the one-liner becomes the opening description inside the created spec.md. Phase-1.5 and Phase-2 rows MUST NOT be specified during the launch program unless the constitution is amended (Principle 32).

---

## 5. Workstream layout (AI-agent-led)

Execution is AI-agent-led under every-PR human review. Agents assigned by lane:

- **Lane A — Spec & Backend** (Claude / Codex): constitution adherence, specs, ADRs, .NET domain, APIs, state machines, database, audit, infrastructure.
- **Lane B — Frontend & Admin** (GLM): Flutter customer app, web storefront, Next.js admin web app, design system, localization.

Rules:

- **Per-feature**: Lane A merges its contract before Lane B starts that feature. No UI against unmerged contracts.
- **Across features**: Lane A and Lane B MAY run in parallel once the contract dependency is satisfied (e.g., while Lane A writes verification backend, Lane B can still build already-contracted catalog UI).
- Human review on **every PR** — no batched reviews, no milestone-exit-only reviews. This is the primary brake on AI drift.
- Agents are forbidden from editing `.specify/memory/constitution.md` or the ADR section of this document. Enforced by CODEOWNERS (spec 001 deliverable).

```
     Lane A (Claude / Codex)             Lane B (GLM)
     +---------------------+             +-----------------------+
     | Spec -> Clarify ->  |  contract   | Consume typed client  |
     | Plan -> Tasks ->    |  merged --> | Build UI against it   |
     | Implement (API)     |             | RTL + Arabic audit    |
     +---------------------+             +-----------------------+
              |                                     |
              +--- audit events ---> audit-log <----+
                           |
                           v
                 +-------------------+
                 | Human review on   |
                 | EVERY PR (brake)  |
                 +-------------------+
```

### 5.1 AI execution guardrails (hard-enforced in CI)

These four guardrails are non-negotiable. They are the only reliable brake on AI agent drift across Claude / Codex / GLM.

1. **Lint + format bar on every PR (blocks merge)**.
   - `.editorconfig` enforced repo-wide.
   - `dotnet format` for backend; `dart format` for Flutter; `eslint` + `prettier` for Next.js admin.
   - CI job `lint-format` must be green. Red = no merge.

2. **Contract tests auto-run on every PR**.
   - Backend emits OpenAPI spec as a build artifact.
   - Generated API client in `packages/shared_contracts` is diffed against the spec.
   - Mismatch = red build. Catches Lane A/B drift at the earliest possible moment.

3. **Constitution + ADR table injected into every agent session**.
   - Root `CLAUDE.md` (and equivalents for Codex / GLM) carries the constitution's enforced principles + the ADR Decisions table.
   - Every `/speckit-*` command prepends this context before spec work begins.
   - Cold-start agent sessions without this context are considered invalid — the work is redone with context.

4. **Agents forbidden from editing constitution + ADRs (human-only)**.
   - CODEOWNERS requires human approval for `.specify/memory/constitution.md` and Section 7 of this file.
   - CI check rejects PRs that modify these paths without a human co-author.
   - Changes to these documents follow the Principle 32 amendment process only.

---

## 6. Tech decisions locked by the constitution

These are baked in. They do not need an ADR and cannot be changed without a constitution amendment (Principle 32).

- **Backend**: .NET 9, modular monolith with explicit domain boundaries, EF Core on PostgreSQL.
- **Frontend**: Flutter stable, single codebase for mobile (Android + iOS) and web storefront.
- **Admin**: a separate web application (framework chosen in ADR-006).
- **Database**: PostgreSQL.
- **Markets**: Egypt and KSA as configurable market records driving tax, payment, shipping, notifications, legal pages (Principle 5). Both live at launch simultaneously.
- **Languages**: Arabic + English; full RTL; ICU message format; Arabic editorial quality (not machine translation) (Principle 4).
- **Brand palette** (Principle 7): primary `#1F6F5F`, secondary `#2FA084`, accent `#6FCF97`, neutral `#EEEEEE`; semantic success/warning/error/info added as needed.
- **State machines**: explicit enum + transition-table pattern for the seven Principle-24 domains.
- **Audit**: centralized audit-log module fed by domain events for every Principle-25 action.
- **Order state**: four orthogonal status fields (order, payment, fulfillment, refund/return) — never merged into a single status.

---

## 7. Architecture decisions (ADRs)

Ten decisions. Seven **Accepted** (lock for v1.0). Three remain **Proposed** (provider selection happens in Stage 7).

> **Lifecycle**: `Proposed` → `Accepted` → (optional) `Superseded by ADR-NNN`.
> An ADR is append-only after acceptance. To change a decision, write a new ADR that supersedes it.
> **This section is CODEOWNERS-protected** — human approval required for edits (Guardrail #4).

### ADR-001 · Monorepo layout · **Accepted**

**Context**: four deployable artifacts (customer mobile app, customer web storefront, backend API, admin web app) plus shared contract and design-token packages. Need one place for contracts without forcing every cloner to pull everything.

**Decision**: **Single monorepo, no build tool**. Layout:

```
apps/
  customer_flutter/    (Flutter mobile + web)
  admin_web/           (Next.js + shadcn/ui)
services/
  backend_api/         (.NET 9, vertical slice + MediatR)
packages/
  shared_contracts/    (generated DTO / enum / error model)
  design_system/       (tokens, variants, RTL rules)
infra/                 (Azure-specific provisioning, migrations runner)
scripts/               (dev, build, test helpers)
```

Nx / Turborepo deferred until cross-package coupling proves the ceremony is worth it.

---

### ADR-002 · Flutter state management · **Accepted**

**Context**: Principle 22 locks Flutter but not the state-management approach. Customer app covers mobile + web with RTL, restricted-product UI, cart/checkout reconciliation.

**Decision**: **Bloc / flutter_bloc**. Strict unidirectional flow, well-understood by AI agents, strong testability. More boilerplate than Riverpod but trade-off is worth it for AI-agent output consistency.

---

### ADR-003 · .NET API style · **Accepted**

**Context**: modular monolith, API versioning, clean domain boundaries.

**Decision**: **Vertical slice + MediatR**. Handler-per-feature maps 1:1 onto Spec-Kit per-feature folders (`specs/###-feature-name/` ↔ `Features/FeatureName/` in the backend). Clean for AI-agent execution — each spec produces a cohesive slice instead of spreading code across layers.

---

### ADR-004 · ORM & migrations · **Accepted**

**Context**: soft deletes, audit hooks, batch/lot + expiry, market-aware pricing, repeatable migrations.

**Decision**: **EF Core 9, code-first migrations**. Hybrid (EF Core + Dapper) deferred until profiling proves a hot path needs it. Soft-delete via query filters; audit hooks via `SaveChangesInterceptor`.

---

### ADR-005 · Search engine · **Accepted**

**Context**: autocomplete, synonyms, Arabic normalization, SKU/barcode search, typo tolerance, facets — behind a service boundary (Principles 12, 26).

**Decision**: **Meilisearch**. Good Arabic normalization out of the box, strong typo tolerance, simple ops, lowest friction for solo + AI-agent execution. Facet + filter API fits storefront needs. Hosted in the same Azure Saudi Arabia Central region as the DB (ADR-010).

---

### ADR-006 · Admin web stack · **Accepted**

**Context**: separate admin web app; table/form-heavy; Arabic + English + RTL.

**Decision**: **Next.js + shadcn/ui**. Strong admin ecosystem (tables, forms, modals), largest AI training corpus (high agent-output quality), SSR not strictly needed behind login but App Router fits cleanly. Admin runs in Lane B under the GLM agent.

---

### ADR-007 · Payment providers · **Proposed**

**Context**: Apple Pay, Visa, MasterCard, Mada, STC Pay, bank transfer, COD, BNPL (Principle 13). Per-market selection. Decided in Stage 7 / Milestone 8.
**Deferral**: Proposed — deferred to Stage 7 (provider selection at integration phase).

**Scope confirmed for v1.0**:
- Both markets at launch (EG + KSA).
- **BNPL at launch both markets**: Tabby + Tamara for KSA; Valu for EG.
- PCI scope: hosted fields / tokenization only. No PAN, CVV, or full track data touches the platform. SAQ-A (or SAQ-A-EP if any redirect hosts).

**Options — Egypt**: Paymob · Fawry · Accept · Kashier · MyFatoorah.
**Options — KSA**: HyperPay · Tap Payments · Moyasar · Checkout.com · PayTabs · MyFatoorah.
**Cross-market aggregators**: Checkout.com · MyFatoorah.

**Decision**: _TBD in Stage 7._

---

### ADR-008 · Shipping providers · **Proposed**

**Context**: rate calculation, shipment creation, tracking, zones, delivery estimates, provider replacement (Principle 14). Decided in Stage 7 / Milestone 8.
**Deferral**: Proposed — deferred to Stage 7 (provider selection at integration phase).

**Options — Egypt**: Bosta · Aramex · Mylerz · R2S · Fetchr · J&T Express EG.
**Options — KSA**: SMSA · Aramex · SPL · DHL · J&T Express KSA · Naqel.
**Regional aggregators**: Shipox · Flixpro.

**Decision**: _TBD in Stage 7_ (recommendation: one primary + one backup per market; aggregator layer optional later).

---

### ADR-009 · Notification & OTP providers · **Proposed (narrowed)**

**Context**: push, email, SMS across OTP, order updates, offers, abandoned cart, restock, price drop, verification, refunds, shipping (Principle 19). Decided in Stage 7 / Milestone 8.
**Deferral**: Proposed — deferred to Stage 7 (provider selection at integration phase).

**Scope confirmed for v1.0**: **SMS + email + push only at launch**. WhatsApp deferred to Phase 1.5 (spec 1.5-f).

**Options**:
- **Email**: Amazon SES · SendGrid · Postmark · Mailgun · Resend.
- **SMS**: Twilio · Unifonic (strong KSA) · MessageBird · Vonage · Infobip.
- **Push**: Firebase Cloud Messaging (FCM); OneSignal as a wrapper with campaigns.

**Decision**: _TBD in Stage 7_ (common stack: SES + Unifonic + FCM aligns with KSA residency and ADR-010).

---

### ADR-010 · Cloud & data residency · **Accepted**

**Context**: KSA PDPL imposes residency/processing rules for KSA-resident personal data. Egypt Law 151/2020 applies to Egyptian residents. Blocks production provisioning.

**Decision**: **Azure Saudi Arabia Central** for all tenants (KSA + EG), single-region with per-market logical partitioning via a `market_code` column on every tenant-owned entity.

Specifics:
1. **Primary cloud**: Azure.
2. **Region**: Saudi Arabia Central for app + DB + search + object storage + backups + logs.
3. **Data-split strategy**: single region, logical per-market partitioning. Schemas identical across markets.
4. **Backups & DR**: in-region only. DR region to be named in spec 001 infra scaffolding.
5. **Payment gateway data flows**: PCI scope kept minimal via hosted fields; no PAN stored. See ADR-007.
6. **Analytics / logs pipeline**: MUST NOT export PII to non-compliant regions. Aggregated, de-identified metrics only may leave region.

---

## 8. Decomposition into Spec-Kit feature specs

Hybrid: Stages 0–2 collapse into **3 foundation specs** (001–003); Stages 3–9 split per-module into **26 module specs** (004–029). Total **29 specs**. Feature folders are created by `./.specify/scripts/bash/create-new-feature.sh "<title>"` and follow `specs/###-<kebab-title>/`.

**Scope note on spec 014 (`customer-app-shell`)**: UI-only. It consumes already-merged contracts from specs 004–013 (identity, catalog, search, pricing, inventory, cart, checkout, orders, tax/invoices, returns). It does NOT re-author business rules, validation, or state transitions. If a business-logic gap is found during UI work, Lane B stops and Lane A amends the owning backend spec — spec 014 never hosts the fix.

| Suggested order | Feature spec | Stage / module | Phase |
|-----------------|--------------|----------------|-------|
| 001 | governance-and-setup | 0 | 1 |
| 002 | architecture-and-contracts | 1 | 1 |
| 003 | shared-foundations | 2 | 1 |
| 004 | identity-and-access | 3.1 | 1 |
| 005 | catalog | 3.2 | 1 |
| 006 | search | 3.3 | 1 |
| 007 | pricing-and-promotions | 3.4 + 6.3 | 1 |
| 008 | inventory | 3.5 | 1 |
| 009 | cart | 3.6 | 1 |
| 010 | checkout | 3.7 | 1 |
| 011 | orders | 3.8 | 1 |
| 012 | tax-and-invoices | 6.8 | 1 |
| 013 | returns-and-refunds | 6.10 (NEW) | 1 |
| 014 | customer-app-shell | 4.1–4.9 | 1 |
| 015 | admin-foundation | 5.1 | 1 |
| 016 | admin-catalog | 5.2 | 1 |
| 017 | admin-inventory | 5.3 | 1 |
| 018 | admin-orders | 5.4 | 1 |
| 019 | admin-customers | 5.5 | 1 |
| 020 | verification | 6.1 | 1 |
| 021 | quotes-and-b2b | 6.2 | 1 |
| 022 | reviews-moderation | 6.4 | 1 |
| 023 | support-tickets | 6.5 | 1 |
| 024 | cms | 6.6 | 1 |
| 025 | notifications | 6.7 | 1 |
| 026 | shipping | 6.9 | 1 |
| 027 | payments-integration | 7 (payments) | 1 |
| 028 | analytics-audit-monitoring | 8 | 1.5 |
| 029 | qa-and-hardening | 9 | 1 |

The number column is *suggested execution order* — `create-new-feature.sh` will assign real sequential numbers at creation time.

---

## 9. Stage-by-stage plan

Each stage lists: **Goal · Deliverables · Principles touched · Blocking ADRs · Exit criteria · Arabic/RTL gate**.

### Stage 0 — Governance & setup · Phase 1

- **Goal**: establish the working model; prevent AI drift; define ownership and done criteria.
- **Deliverables**: this plan; ten ADRs in Section 7 (seven now Accepted); Definition of Done (Section 11); feature spec `001-governance-and-setup` including `CLAUDE.md` with constitution + ADR injection pattern, CODEOWNERS enforcing Guardrail #4, CI skeleton enforcing Guardrails #1 and #2.
- **Principles touched**: 22, 23, 28, 29, 30, 31.
- **Blocking ADRs**: none (ADRs 001-006 + 010 already Accepted at plan approval).
- **Exit criteria**: DoD approved; feature spec 001 approved; `CLAUDE.md` + CODEOWNERS + CI pipeline green on `main`.
- **Arabic/RTL gate**: N/A (no UI).

### Stage 1 — Architecture & contracts · Phase 1

- **Goal**: lock architecture before coding depth grows.
- **Deliverables**:
  - Finalize any remaining ADRs (007, 008, 009 remain Proposed — they're resolved in Stage 7).
  - API design rules (envelope, error model, pagination, filtering, idempotency keys, versioning, webhook security).
  - Domain overview + **ERD / entity map** (tables, ownership fields for multi-vendor-readiness, soft-delete columns, audit-hook points, market-scope columns).
  - Permissions matrix v1 (roles × permissions, includes B2B approver/buyer distinction).
  - **State models** for all seven Principle-24 domains (verification, cart/checkout, payment, order, shipment, return/refund, quote) — each with valid states, triggers, allowed actors, failure handling, retry handling. These are **exit-blocking**, not drafts.
  - **Testing strategy**: unit vs integration vs E2E split; coverage expectations per layer; test-data / seeding strategy; contract-test approach for Lane A/B boundary.
  - **CI/CD bootstrap**: lint + type-check + test + build pipeline green on `main`; branch protection; artifact layout (per ADR-001); secrets injection pattern; no direct pushes to `main`.
  - **Reservation semantics** for inventory (see Stage 3.5) embedded in cart/checkout/order state models.
  - Feature spec `002-architecture-and-contracts`.
- **Principles touched**: 6, 17, 22, 23, 24, 25, 29, 30.
- **Blocking ADRs**: none (all architectural ADRs Accepted at plan approval).
- **Exit criteria**: all seven state models merged; API rules merged; ERD merged; permissions matrix approved; testing strategy merged; CI pipeline green.
- **Arabic/RTL gate**: localization-key strategy decided (ICU format; Arabic treated as a primary, not a fallback, in EG/KSA builds).

### Stage 2 — Shared foundations · Phase 1

- **Goal**: reusable contracts and design tokens before feature duplication begins.
- **Deliverables**:
  - Shared contract library (enums, statuses, role/permission names, DTO envelope, paging, filtering, error model) consumable by both Flutter and .NET.
  - Design-system foundation: constitution palette as tokens; typography, spacing, icon rules; button/input/card/badge/modal variants; empty/loading/error/restricted patterns; RTL behavior rules.
  - Localization foundation: Arabic + English key structure; locale-aware formatting; fallback behavior.
  - **Audit-log module** (Principle 25): domain-event sink, append-only storage, actor + before/after + correlation-id schema, admin read API. Every Stage 3–6 module consumes this; no module rolls its own.
  - **File-storage abstraction** (media for catalog, docs for verification, invoice PDFs): provider-agnostic interface, dev stub (local disk), prod provider (Azure Blob Storage in-region) swapped in Stage 7.
  - **PDF generation abstraction** (tax invoices, return confirmations): same pattern — interface + dev stub here, prod wiring Stage 7.
  - Feature spec `003-shared-foundations`.
- **Principles touched**: 4, 7, 24, 25, 27, 28.
- **Blocking ADRs**: none.
- **Exit criteria**: any new module can consume shared contracts + tokens + audit + storage + PDF without copying code; palette matches constitution exactly.
- **Arabic/RTL gate**: Arabic strings render correctly in both apps at token level.

### Stage 3 — Backend core commerce domains · Phase 1

Goal: end-to-end browse-to-order in a controlled test environment. Each section is its own feature spec.

- **3.1 Identity & access**: registration, login, password auth, phone OTP, password reset, profile creation, sessions, role framework, customer vs admin separation. *Principles 3, 9, 24.*
- **3.2 Catalog**: categories, brands, products, media, documents, attributes/specs, restriction metadata, rich content fields, active/inactive states. *Principles 8, 10, 21.*
- **3.3 Search**: keyword, category/brand/offer/stock filters, sort, autocomplete, SKU/barcode, Arabic normalization. Uses Meilisearch (ADR-005). *Principles 12, 26.*
- **3.4 Pricing & tax**: base / compare-at / discount price; market-aware resolution; **VAT/tax computation for EG and KSA**; promotion **engine primitives only** (price-resolution pipeline, stacking rules, exclusion flags). Promotion UX, coupon lifecycle, campaigns, business/tier pricing authoring all live in Stage 6.3 — not here. Spec 007 (`pricing-and-promotions`) spans both stages but is split into two phases inside the spec: 007-a engine (Stage 3.4), 007-b authoring + campaigns (Stage 6.3). *Principles 10, 18.*
- **3.5 Inventory**: stock quantities; warehouse-ready design; available-to-sell; **reservation semantics (default)**: soft-hold created at checkout-start with a time-bounded TTL (e.g., 15 min); hard-commit on payment-authorized; release on checkout abandon / TTL expiry / payment-failed. Exact TTL and conflict-resolution rules are locked in the Stage 1 cart/checkout state model. Also: low-stock thresholds; batch/lot fields; expiry-ready model. *Principle 11.*
- **3.6 Cart**: guest cart, logged-in cart, merge, coupon application, validation. *Principles 3, 24.*
  - *"Save for later"* is explicitly **Phase 1.5** — out of V1.
- **3.7 Checkout**: address, shipping, billing, payment initiation, order preview, validation, restricted-product enforcement, stock revalidation; **invoice linkage defined here**. *Principles 8, 13, 18, 24.*
- **3.8 Orders**: placement, items, status history, payment status, fulfillment status, refund/return state (separate field), invoice link, reorder basics. Four orthogonal status fields — never merged. *Principles 17, 24, 25.*

**Blocking ADRs**: none (001–006 all Accepted).
**Exit criteria**: anonymous user browses → registers → adds items → checks out (stub payment) → views order and downloads invoice.
**Arabic/RTL gate**: every API response carries localized display strings or localization keys; responses smoke-tested in Arabic.

### Stage 4 — Customer app core flows · Phase 1

- **Goal**: production-quality customer app shell for mobile + web storefront.
- **Scope**: single feature spec `014-customer-app-shell` covering shell, auth, home, listing, detail, cart, checkout, orders, more-menu. Vague items resolved:
  - *"Brand pages if included"* → **Phase 1** (brand-filtered listing; no dedicated brand page).
  - *"Recommended modules placeholders"* on Home → **Phase 1.5**. V1 Home = best-sellers + featured categories + banners only.
  - *"Trust indicators"* → **Phase 1**, static content (payment badges, licensed-clinic, verified-seller label) — no dynamic logic.
- **Principles touched**: 3, 4, 7, 8, 27.
- **Blocking ADRs**: none (002 Accepted — Bloc).
- **Exit criteria**: every Phase 1 flow reachable from cold start on Android, iOS, web; loading/empty/error/success/restricted states verified on each.
- **Arabic/RTL gate**: full-screen audit in Arabic with RTL mirroring on every route.

### Stage 5 — Admin core operations · Phase 1

- **Modules**: 5.1 admin foundation · 5.2 catalog · 5.3 inventory · 5.4 orders · 5.5 customers.
- **Stack**: Next.js + shadcn/ui (ADR-006).
- **Principles touched**: 20, 21, 25, 26, 27.
- **Blocking ADRs**: none (006 Accepted).
- **Exit criteria**: ops can upload a product, adjust stock, transition an order, view a customer profile — all emitting audit-log entries.
- **Arabic/RTL gate**: admin UI parity-tested in Arabic.

### Stage 6 — Business modules & advanced operations · Phase 1

- **Modules** (new **6.10** added):
  - 6.1 Verification (submission, doc upload, review queue, approve/reject/request-info, history/audit, expiry support).
  - 6.2 Quotes & B2B (quote requests, admin quote creation, revisions, acceptance, quote→order conversion, company account basics, PO/reference number).
  - 6.3 Promotions (coupons, scheduled promotions, banner-linked campaigns, business-pricing foundations, tier pricing).
  - 6.4 Reviews moderation (queue, hide/delete, abuse notes, verified-buyer enforcement).
  - 6.5 Support (ticket creation/list/detail, reply flow, category tagging, order-linked issues, SLA fields).
  - 6.6 CMS (banners, featured sections, FAQ, legal pages, blog/educational skeleton, localized publishing).
  - 6.7 Notifications (template management, event-triggered messages, campaign basics, preference management). **SMS + email + push only at launch; WhatsApp deferred to Phase 1.5.**
  - 6.8 Finance & invoices (tax invoice generation, invoice download, exportable finance views, refund record visibility).
  - 6.9 Shipping settings (provider settings, market rules, delivery methods, fee configuration, shipment state mapping).
  - **6.10 Returns & refunds (NEW)** — submission, eligibility check, admin decision flow, refund execution, audit, full state model.
- **Principles touched**: 8, 9, 15, 16, 17, 18, 19, 20, 21, 24, 25.
- **Blocking ADRs**: 007, 008, 009 (providers selected in Stage 7).
- **Exit criteria**: every module has spec + backend + admin UI + customer-facing surface where applicable.
- **Arabic/RTL gate**: every customer-visible notification template and PDF invoice localized and RTL-mirrored.

### Stage 7 — Integrations · Phase 1

- **Goal**: swap dev stubs for production providers behind already-built abstraction layers. Nothing here is first-integration — every module consuming an external service has been talking to a stub since Stage 2 or Stage 3.
- **In scope here**: OTP/SMS, email, push, payments (primary + backup per market, including BNPL Tabby/Tamara KSA + Valu EG), shipping (primary + backup per market), storage (swap local-disk for Azure Blob in-region), PDF generation (swap dev renderer for production renderer). **Not in scope: WhatsApp** (Phase 1.5).
- **Integration order** (driven by risk, not alphabetical):
  1. **Storage** (Azure Blob Storage, in-region) — unblocks catalog media at real scale; lowest external-dependency risk.
  2. **OTP/SMS** — gates registration; verify Arabic templates + EG/KSA deliverability early.
  3. **Email** — order, verification, invoice paths.
  4. **Push** — FCM/APNs; depends on customer app build signing being in place.
  5. **Payments** — highest risk; run staging reconciliation loop before moving on. BNPL wired here (Tabby + Tamara + Valu).
  6. **Shipping** — rates + label + tracking; webhook ingestion.
  7. **PDF generation** — production renderer for invoices and return confirmations (Arabic must render correctly — this is a known trap).
- **Principles touched**: 11, 13, 14, 18, 19.
- **Blocking ADRs**: 007, 008, 009 (010 already Accepted).
- **Exit criteria**: staging runs every critical flow against real providers; no hardcoded provider references outside abstraction layers; payment reconciliation job runs nightly in staging without discrepancies for one week.
- **Arabic/RTL gate**: localized SMS/email templates + PDF invoices verified in Arabic with correct number/date/currency formatting for EG and KSA.

### Stage 8 — Analytics, audit & monitoring · Phase 1.5 (not launch-blocking)

- **Scope**: event tracking, conversion funnel, search analytics, order/quote/verification/support metrics; advanced observability dashboards; payment-failure alerts; integration error alerts; uptime checks; structured logs.
- **Principles touched**: 25, 28.
- **Note**: audit logging itself (Principle 25) is Phase 1 and built inline with each module in Stages 3–6. What ships in Phase 1.5 is the **analytics dashboards and advanced observability**. Minimum Phase 1 = structured logs + basic uptime.

### Stage 9 — QA & hardening · Phase 1

- **QA tracks**: functional · localization · security · reliability · performance.
- **Exit criteria**: launch-readiness checklist (Section 13) passes.
- **Arabic/RTL gate**: full regression in Arabic by the named Arabic editorial reviewer (Risk 11 must be resolved before this gate).

---

## 10. Milestones

Nine milestones, each ~2–3 weeks with AI-agent execution + every-PR human review. Every milestone must exit with an Arabic/RTL smoke test. Dates are relative weeks from approval of spec `001-governance-and-setup`.

| # | Milestone | Covers | Weeks | Cumulative week |
|---|-----------|--------|-------|-----------------|
| 1 | Foundation | Stages 0–2; specs 001–003; CI green; ERD merged; CLAUDE.md + CODEOWNERS live | 3 | W3 |
| 2 | Identity + catalog + search | 3.1–3.3; specs 004–006 | 3 | W6 |
| 3 | Pricing + inventory + cart | 3.4–3.6; specs 007-a, 008, 009 | 2 | W8 |
| 4 | Checkout + orders + tax/invoice + returns | 3.7, 3.8, 6.8, 6.10; specs 010–013 | 3 | W11 |
| 5 | Customer app shell (UI only — spec 014 consumes 004–013) | Stage 4; spec 014 | 3 | W14 |
| 6 | Admin foundation + core admin modules | Stage 5; specs 015–019 | 3 | W17 |
| 7 | Verification + B2B + reviews + support + CMS + promo UX | 6.1–6.6 incl. spec 007-b; specs 020–024 | 3 | W20 |
| 8 | Notifications + shipping + payments integration (incl. BNPL) | 6.7, 6.9, Stage 7; specs 025–027 | 3 | W23 |
| 9 | QA, hardening, launch prep | Stage 9; spec 029; baseline Stage 8 observability | 2 | W25 (launch) |

Total: ~25 focused weeks. Do not compress Milestones 1, 4, or 9.

```
 Week:      1    3    5    7    9   11   13   15   17   19   21   23   25
            |    |    |    |    |    |    |    |    |    |    |    |    |
 M1 Found  [===]
 M2 IdCatS      [===]
 M3 PrInCa           [==]
 M4 ChkOrT              [===]
 M5 CustApp                  [===]
 M6 Admin                         [===]
 M7 B2B/CMS                            [===]
 M8 Integs                                  [===]
 M9 QA/Lnch                                      [==]
                                                     ^ Week 25 launch
```

Slack is intentionally zero between milestones — the constitution requires park-safe exits (Risk 9), so any slip consumes the next milestone's budget rather than eroding DoD.

---

## 11. Definition of Done

Copy into every feature spec. A module is done only when:

- [ ] Spec approved and merged (`/speckit-specify` + `/speckit-clarify`).
- [ ] Phase label assigned (1 / 1.5 / 2).
- [ ] Business rules implemented.
- [ ] Happy path + all edge cases documented in acceptance criteria.
- [ ] Validation rules implemented at the API boundary.
- [ ] Permissions enforced and matched to the permissions matrix.
- [ ] State model documented if the domain is in Principle 24's list.
- [ ] Audit events emitted if the action is in Principle 25's list.
- [ ] Multi-vendor-readiness reviewed — no hardcoded single-vendor assumptions in ownership, payout, commission, role structure, inventory ownership, or fulfillment ownership (Principle 6).
- [ ] Arabic + English strings complete; RTL smoke test passed.
- [ ] Market-config parameters surfaced for EG / KSA where relevant.
- [ ] Tax / invoice touch-points identified if module is checkout/order/pricing-adjacent.
- [ ] Loading / empty / error / success / restricted-state UI present where applicable.
- [ ] Tests added (unit + integration where scoped) per the Stage 1 testing strategy.
- [ ] CI green on the merging branch (lint + type-check + unit + integration + contract-test — Guardrails #1 and #2).
- [ ] **Lint + format bar green** (enforced by CI, cannot merge red — Guardrail #1).
- [ ] **Constitution + ADR context present in the session prompt** used to author this module (Guardrail #3).
- [ ] **No edits to `constitution.md` or ADRs** outside a human-approved Principle 32 amendment (Guardrail #4).
- [ ] Structured logs emitted for every state transition and external-provider call.
- [ ] Human code review passed (every PR, not batched).

---

## 12. Risk register

Risks ordered by severity given the AI-agent-led execution model.

| # | Risk | Mitigation |
|---|------|------------|
| **1** | **AI agents drift from spec (top risk given execution model)** | Four hard guardrails (Section 5.1): lint + format bar on every PR; contract tests auto-run; constitution + ADR table injected into every agent session; CODEOWNERS forbids agent edits to constitution/ADRs. Plus every-PR human review. |
| **10** | **Agent idiom drift across Claude / Codex / GLM** | Lint + format bar enforced (Guardrail #1); shared `CONTRIBUTING.md` with style guide per ADR; pinned framework/language versions; contract tests catch API-shape divergence. |
| **11** | **Arabic editorial reviewer unfilled (launch blocker)** | Source a native-level reviewer before Milestone 2 exit. Without them, Milestone 9 Arabic/RTL regression cannot sign off. Escalate to risk ledger weekly if unfilled. |
| 2 | Frontend invents backend behavior | Lane A leads; contracts shipped before UI; view models consume only typed contracts from the shared package; contract tests fail fast on divergence. |
| 3 | Payment/order race conditions | Idempotency keys on every mutating endpoint; reservation rules at checkout; webhook replay discipline; nightly reconciliation job (Stage 7 exit criterion). |
| 4 | Arabic support treated late | RTL/Arabic gate at every milestone exit; shared tokens enforce RTL from Milestone 1. |
| 5 | Admin underbuilt | Admin is Milestone 6 (before launch prep). No launch without Stage 5 exit met. |
| 6 | **B2B scope quietly reduced post-lock** | Constitution Principle 9 makes B2B mandatory V1. Any reduction requires a constitution amendment (Principle 32) — not a silent decision. |
| 7 | **Data residency breach at production cutover** | ADR-010 Accepted: Azure Saudi Arabia Central. No production resource provisioned outside the approved region. CI check on Terraform/Bicep manifests to block out-of-region resources. |
| 8 | **Vendor-drift (single-vendor assumptions leak into schema)** | DoD checkbox per module; quarterly audit of ownership columns on `products`, `inventory`, `orders`, `payouts`. |
| 9 | Solo-dev burnout / schedule slip | Milestones are park-safe — each exits at a demonstrable state. Freeze new scope between milestones. |

---

## 13. Launch-readiness checklist

### Product
- [ ] All Phase 1 scope in Section 8 functional.
- [ ] Market rules reviewed for EG and KSA (tax, payment methods, return policies, legal pages).
- [ ] Restricted-product logic verified end-to-end.
- [ ] Returns/refunds flow operational (Stage 6.10).
- [ ] B2B quote → order conversion tested.

### Engineering
- [ ] Staging stable.
- [ ] Migrations repeatable on a clean DB.
- [ ] Backup and restore verified (in-region per ADR-010).
- [ ] Secrets managed (no secrets in repo; Azure Key Vault in-region).
- [ ] Rate limits configured on public endpoints.
- [ ] ADRs 001–006 + 010 **Accepted** (locked at v1.0); 007, 008, 009 resolved by Stage 7 exit.
- [ ] Lint + format bar green on `main` (Guardrail #1).
- [ ] Contract tests green on `main` (Guardrail #2).
- [ ] CODEOWNERS enforcement verified by attempting a forbidden edit (Guardrail #4).

### Integrations
- [ ] Payment providers live-ready per market (incl. BNPL: Tabby/Tamara KSA + Valu EG).
- [ ] Shipping providers tested per market.
- [ ] OTP/SMS delivered to test numbers in EG and KSA.
- [ ] Email delivered with correct Arabic rendering.
- [ ] Push verified on Android + iOS.
- [ ] PDF invoices correct in Arabic and English.
- [ ] (WhatsApp NOT in scope for launch — Phase 1.5.)

### Operations
- [ ] Catalog loaded.
- [ ] Support team trained.
- [ ] Admin roles assigned per permissions matrix.
- [ ] Verification SOP ready.
- [ ] Refund SOP ready.
- [ ] Order-ops SOP ready.

### QA
- [ ] Full regression passed.
- [ ] Arabic editorial QA passed by a named human reviewer (Risk 11 resolved; not MT-checked).
- [ ] Web + mobile (Android + iOS) smoke tests passed.
- [ ] Admin permissions matrix tested.

### Compliance
- [ ] KSA PDPL checks passed (residency + privacy notices).
- [ ] Egypt Law 151/2020 checks passed.
- [ ] Egypt VAT invoice format verified with an accountant.
- [ ] Legal pages in Arabic + English reviewed.
- [ ] **Azure Saudi Arabia Central region confirmed for all tenants (KSA + EG); no out-of-region data paths.**

### Monitoring
- [ ] Uptime monitor live.
- [ ] Error tracking active.
- [ ] Structured logs accessible.
- [ ] Payment-failure alerts firing.

---

## 14. Next actions

In this exact order:

1. **Approved — done.** This plan is v1.0.
2. **Author feature spec `001-governance-and-setup`** via `/speckit-specify`. Deliverables: DoD approved, repo layout scaffolded per ADR-001, `CLAUDE.md` with constitution + ADR injection pattern (Guardrail #3), CODEOWNERS enforcing Guardrail #4, CI skeleton enforcing Guardrails #1 and #2.
3. **Source the Arabic editorial reviewer** (Risk 11). Target: named before Milestone 2 exit (W6).
4. **Author feature spec `002-architecture-and-contracts`** — state models, permissions matrix, ERD, API rules, testing strategy, CI/CD bootstrap. Remaining Proposed ADRs (007, 008, 009) addressed later in Stage 7 with provider-specific specs.
5. **Author feature spec `003-shared-foundations`** — shared contract library, design tokens, localization scaffolding, audit-log module, storage + PDF abstractions.
6. **Begin Milestone 2** (identity + catalog + search) with per-module feature specs 004–006.
7. After each milestone exit: run the Arabic/RTL gate, confirm CI green on `main`, re-check ADR status table, then start the next milestone's first spec — do not run `/speckit-specify` for milestone N+1 before N exits.

**ADR dependency map** (which ADR unblocks which stage):

```
 ADR-001 (monorepo)   [Accepted]  --> Stage 0,1  (repo layout + CI)
 ADR-002 (Flutter SM) [Accepted]  --> Stage 1,4  (customer app)
 ADR-003 (API style)  [Accepted]  --> Stage 1,3  (backend modules)
 ADR-004 (ORM)        [Accepted]  --> Stage 1,3  (DB migrations)
 ADR-005 (Search)     [Accepted]  --> Stage 3.3  (Meilisearch)
 ADR-006 (Admin web)  [Accepted]  --> Stage 1,5  (Next.js admin)
 ADR-007 (Payments)   [Proposed]  --> Stage 7    (payment integration)
 ADR-008 (Shipping)   [Proposed]  --> Stage 6.9, 7
 ADR-009 (Notif/OTP)  [Proposed]  --> Stage 6.7, 7  (SMS+email+push only)
 ADR-010 (Residency)  [Accepted]  --> Azure Saudi Arabia Central
```

---

## 15. Appendix — disposition of the original ChatGPT plan

The ChatGPT "AI-Build Execution Plan" had useful structure but material gaps. This appendix documents how each issue was handled.

| Original item | Disposition |
|---------------|-------------|
| Proposed location `specs/11_delivery_plan/11_00_implementation_plan.md` | Moved to `docs/implementation-plan.md` to avoid Spec-Kit's `###-feature-name/` numbering collision. |
| Returns/refunds only in launch-readiness and QA | Added as module **6.10** with its own state model. |
| State machines listed as "drafts" in Stage 1 | Promoted to **blocking exit gate** at end of Stage 1 for all seven Principle-24 domains. |
| Tax/invoice only in Stage 6.8 | Folded into **Stage 3.4 (pricing)** and **Stage 3.7 (checkout)** as well. |
| Risk 6 (B2B may slip) | Rewritten as Risk 6 "B2B scope quietly reduced post-lock" — mitigation is constitutional amendment, not quiet rescope. |
| Phase labels missing on most scope items | Every scope item now carries a phase label (1 / 1.5 / 2). |
| No Arabic-first enforcement | Arabic/RTL gate at every milestone exit and every stage exit. |
| No data-residency gate | **ADR-010 Accepted**: Azure Saudi Arabia Central; both markets in-region. |
| Multi-vendor-ready not enforced | Added as DoD checkbox on every module. |
| Six parallel workstreams | Collapsed to two lanes for AI-agent execution: Lane A = Claude/Codex, Lane B = GLM. |
| "Save for later later if needed" | Deferred to **Phase 1.5** (spec 1.5-a); typo removed. |
| "Brand pages if included" | Included in **Phase 1** (brand-filtered listing; no dedicated page). |
| "Recommended modules placeholders" on Home | Deferred to **Phase 1.5** (spec 1.5-b). |
| "Trust indicators" | Included in **Phase 1** as static content, no dynamic logic. |
| "Reporting and optimization" appearing only in Section 2 | Absorbed into **Stage 8 (Phase 1.5)**. |
| Admin stack unnamed | **ADR-006 Accepted**: Next.js + shadcn/ui. |
| Six milestones assuming parallel teams | Resized to **nine AI-agent-led milestones** of 2–3 weeks each. |
| Lane A/B rule contradicted milestone overlap | Rewritten: serial **per feature**, parallel **across features** once contract is merged. |
| Testing strategy + CI/CD had no home | Made explicit deliverables of spec 002; DoD now requires CI green. |
| ERD / data model had no home | Named as a spec 002 deliverable with ownership / multi-vendor / audit-hook columns required. |
| Audit-log module referenced but homeless | Assigned to spec 003 (`shared-foundations`); DoD forbids per-module rolls. |
| Storage + PDF listed in Stage 7 only | Moved to spec 003 as abstractions with dev stubs; Stage 7 only swaps stubs for production providers. |
| Promotions double-booked in 3.4 and 6.3 | Spec 007 split into 007-a (engine, Stage 3.4) and 007-b (UX/campaigns, Stage 6.3). |
| Inventory reservation rules undefined | Default rule declared: soft-hold on checkout-start with TTL, hard-commit on payment-authorized. |
| ADR-007 had no PCI guidance | Added PCI cross-reference: hosted fields / tokenization only; SAQ-A scope. |
| Program start was a placeholder date | Switched to relative Week 1..25 anchored to spec 001 approval. |
| "Next actions" stopped at spec 003 | Extended to post-Milestone-1 cadence; added ADR dependency map. |
| AI execution model was implicit | Section 5 rewritten as AI-agent-led (Claude/Codex Lane A, GLM Lane B); Section 5.1 added with four hard guardrails. |
| Risk 1 (AI drift) was #1-of-9 | Elevated to top of register with four concrete CI guardrails + every-PR human review. |
| ADR-004 had no default | **Accepted**: EF Core 9 code-first. |
| WhatsApp was ambiguous | **Deferred to Phase 1.5** explicitly (spec 1.5-f); SMS + email + push only at launch. |
| ADR-010 was BLOCKER | **Resolved Accepted**: Azure Saudi Arabia Central. |
| ADR-001 open | **Resolved Accepted**: single monorepo, no build tool. |
| ADR-002 open | **Resolved Accepted**: Bloc / flutter_bloc. |
| ADR-003 open | **Resolved Accepted**: vertical slice + MediatR. |
| ADR-005 open | **Resolved Accepted**: Meilisearch. |
| ADR-006 open | **Resolved Accepted**: Next.js + shadcn/ui. |
| iOS at launch unclear | **Confirmed**: full parity Android + iOS + web at launch. |
| Markets strategy unclear | **Confirmed**: EG + KSA simultaneously at launch. |
| BNPL scope unclear | **Confirmed Phase 1 both markets**: Tabby + Tamara (KSA), Valu (EG). |

---

## Changelog

- **2026-04-20** — A1 (Environments / Docker / Seed framework) promoted from trailing appendix to first-class Phase 1A content (§4 table + §4.1 A1 sub-section). Full retrofit rationale: `docs/missing-env-docker-plan.md`.
- **2026-04-20** — `specs/phase-1B/` (004–008 draft specs) removed; Phase 1B spec authoring restarts against the refreshed plan.
- **2026-04-20** — Plan-fidelity audit: extended A1 config/seed/Docker patterns across all specs 009–027; added **Phase 1C-Infra** (admin_web Dockerfile + CI) and **Phase 1E E1** (`infrastructure-integration` — Azure Container Apps IaC, Key Vault provisioning, `deploy-staging.yml` wiring, Flutter-web hosting decision); made Phase 1C → 1B dependencies explicitly require "contract merged to `main`"; extended spec 029 with load-test on Staging, Production `seed --mode=dry-run` smoke, and multi-container health verification. **No Phase 1A re-implementation required** — all changes are plan-text fidelity fixes.
