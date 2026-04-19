# Implementation Plan — Dental Commerce Platform

**Status**: Draft v0.2 (consolidated single-file plan)
**Authority**: Subordinate to `.specify/memory/constitution.md` (v1.0.0, 2026-04-19)
**Supersedes**: the ChatGPT "AI-Build Execution Plan"
**Owner**: Mohamed Khira
**Last updated**: 2026-04-19

---

## Table of Contents

1. [Executive summary](#1-executive-summary)
2. [How this doc fits the workspace](#2-how-this-doc-fits-the-workspace)
3. [Delivery philosophy](#3-delivery-philosophy)
4. [Phase model](#4-phase-model)
5. [Workstream layout (solo/small team)](#5-workstream-layout-solosmall-team)
6. [Tech decisions locked by the constitution](#6-tech-decisions-locked-by-the-constitution)
7. [Open decisions (ADRs)](#7-open-decisions-adrs)
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

A bilingual (Arabic + English) dental commerce platform for Egypt and Saudi Arabia targeting dentists, clinics, dental labs, students, and general consumers. The stack is locked: Flutter for customer mobile and web storefront, .NET for backend, PostgreSQL for the database, a separate web app for admin. Launch is single-vendor but the architecture must remain multi-vendor-ready. V1 is not a demo — it ships with real B2B flows, verification, restricted-product eligibility, tax invoices, inventory depth, and an operationally complete admin.

This plan converts those constraints into a concrete delivery sequence for a solo developer (with AI assistance), structured as ten stages, nine milestones, and ~29 feature specs managed via Spec-Kit. Every scope item carries an explicit phase label (1 / 1.5 / 2). Arabic and RTL are gates at every milestone exit, not a late-stage QA pass. Data residency for KSA PDPL is a hard prerequisite before any production infrastructure is provisioned.

---

## 2. How this doc fits the workspace

- **Constitution** (`.specify/memory/constitution.md`) is non-negotiable. If this plan contradicts it, the constitution wins and this plan must be revised (Principle 31).
- This is a **program-level roadmap**, not a feature spec. Feature-level work is authored through `/speckit-specify`, `/speckit-clarify`, `/speckit-plan`, `/speckit-tasks`, `/speckit-implement` into `specs/###-feature-name/` folders (numbered by `.specify/scripts/bash/create-new-feature.sh`).
- Git hooks in `.specify/extensions.yml` auto-commit after each `/speckit-*` step. Use those instead of ad-hoc commits to keep spec history coherent.
- No code is produced at the roadmap level. The roadmap orchestrates sequence, dependencies, and gates.

---

## 3. Delivery philosophy

**Spec → contract → backend logic → UI → integrations → QA.**

Do not let UI implementation invent business logic. Business rules, states, contracts, and validations must be defined before any code touches them.

Corollary rules:

- State-model authoring is a blocking exit gate for any Principle-24 domain (verification, cart/checkout, payment, order, shipment, return/refund, quote) before code in that domain begins.
- Arabic/RTL is built into every milestone — never retrofitted.
- Every module must be reviewed for multi-vendor-ready assumptions (no hardcoded single-vendor logic in ownership, payout, commission, role structure, inventory ownership, or fulfillment ownership).
- No `Proposed`-state ADR may be coded against in production.

---

## 4. Phase model

From constitution Principle 30:

| Phase | Intent | Where in this plan |
|------|--------|--------------------|
| **Phase 1** | Strong launch-ready platform | Stages 0–9, Milestones 1–9 |
| **Phase 1.5** | Optimization and operational depth | "Deferred" notes inside each stage + full Analytics/Observability in Stage 8 |
| **Phase 2** | Marketplace / vendor expansion | Out of scope here; explicitly excluded: vendor portal, commissions, payouts, split checkout |

Every scope bullet in this doc is Phase 1 unless labeled otherwise.

---

## 5. Workstream layout (solo/small team)

The original ChatGPT plan assumed six parallel workstreams. Solo-developer reality collapses to **two serial lanes**:

- **Lane A — Spec & Backend**: constitution, specs, ADRs, .NET domain, APIs, state machines, database, audit, infrastructure.
- **Lane B — Frontend & Admin**: Flutter customer app, web storefront, admin web app, design system, localization.

Rules:

- Lane A leads. Lane B pulls only from completed Lane A contracts.
- Only one lane is "hot" at a time in solo mode.
- Contractors may later parallelize Lane B modules, but never ahead of their matching Lane A contract merge.

---

## 6. Tech decisions locked by the constitution

These are baked in. They do not need an ADR and cannot be changed without a constitution amendment (Principle 32).

- **Backend**: .NET 9, modular monolith with explicit domain boundaries, EF Core on PostgreSQL.
- **Frontend**: Flutter stable, single codebase for mobile (Android + iOS) and web storefront.
- **Admin**: a separate web application (framework to be chosen in ADR-006).
- **Database**: PostgreSQL.
- **Markets**: Egypt and KSA as configurable market records driving tax, payment, shipping, notifications, legal pages (Principle 5).
- **Languages**: Arabic + English; full RTL; ICU message format; Arabic editorial quality (not machine translation) (Principle 4).
- **Brand palette** (Principle 7): primary `#1F6F5F`, secondary `#2FA084`, accent `#6FCF97`, neutral `#EEEEEE`; semantic success/warning/error/info added as needed.
- **State machines**: explicit enum + transition-table pattern for the seven Principle-24 domains.
- **Audit**: centralized audit-log module fed by domain events for every Principle-25 action.
- **Order state**: four orthogonal status fields (order, payment, fulfillment, refund/return) — never merged into a single status.

---

## 7. Open decisions (ADRs)

Ten open decisions, embedded inline. None may be coded against in production until marked **Accepted**. Fill in `Decision` when resolved, flip status, and commit.

> **Lifecycle**: `Proposed` → `Accepted` → (optional) `Superseded by ADR-NNN`.
> An ADR is append-only after acceptance. To change a decision, write a new ADR that supersedes it.

### ADR-001 · Monorepo layout · **Proposed**

**Context**: four deployable artifacts (customer mobile app, customer web storefront, backend API, admin web app) plus shared contract and design-token packages. Need one place for contracts without forcing every cloner to pull everything. Blocks Stage 1.

**Options**:
- **A — Single monorepo, no tool**: `apps/`, `services/`, `packages/`. Simplest; no build graph.
- **B — Monorepo with Nx / Turborepo**: build graph, caching; overkill until cross-package coupling is real.
- **C — Polyrepo**: cleanest isolation; painful contract versioning.
- **D — Two repos**: .NET repo (backend + admin) and Flutter repo (customer + mirrored generated contracts).

**Decision**: _TBD_.

---

### ADR-002 · Flutter state management · **Proposed**

**Context**: Principle 22 locks Flutter but not the state-management approach. Customer app covers mobile + web with RTL, restricted-product UI, cart/checkout reconciliation. Blocks Stage 1 and Milestone 5.

**Options**:
- **A — Riverpod**: compile-time safe, no BuildContext, strong testability, common default.
- **B — Bloc / flutter_bloc**: strict unidirectional flow, more boilerplate, common in enterprise.
- **C — Provider**: simple, limited for complex async graphs.
- **D — GetX**: opinionated; risks non-idiomatic AI-generated code.

**Decision**: _TBD_.

---

### ADR-003 · .NET API style · **Proposed**

**Context**: modular monolith, API versioning, clean domain boundaries. Blocks Stage 1 and Milestone 2.

**Options**:
- **A — Minimal APIs** (.NET 8/9): low boilerplate; group endpoints per module.
- **B — Controllers (MVC)**: mature, well-known; more ceremony.
- **C — FastEndpoints**: REPR pattern; extra dependency.
- **D — Vertical slice + MediatR**: handler-per-feature; maps onto Spec-Kit per-feature folders.

**Decision**: _TBD_.

---

### ADR-004 · ORM & migrations · **Proposed**

**Context**: soft deletes, audit hooks, batch/lot + expiry, market-aware pricing, repeatable migrations. Blocks Stage 1 and Milestone 2.

**Options**:
- **A — EF Core 9** with code-first migrations (default recommendation).
- **B — Dapper + DbUp/Flyway**: more control, more work.
- **C — Hybrid**: EF Core for CRUD, Dapper for hot read paths later if profiling demands it.

**Decision**: _TBD_ (recommendation: start with A, adopt hybrid only when hot paths prove it).

---

### ADR-005 · Search engine · **Proposed**

**Context**: autocomplete, synonyms, Arabic normalization, SKU/barcode search, typo tolerance, facets — behind a service boundary (Principles 12, 26). Blocks module 3.3 and Milestone 2.

**Options**:
- **A — PostgreSQL FTS** with `pg_trgm` + Arabic config: no extra infra; mediocre stemming; no native synonyms.
- **B — Meilisearch**: strong typo tolerance, good Arabic normalization, simple ops.
- **C — Typesense**: comparable to Meilisearch; strong filtering.
- **D — OpenSearch / Elasticsearch**: most powerful; heaviest ops; best Arabic analyzers via ICU plugin.

**Decision**: _TBD_.

---

### ADR-006 · Admin web stack · **Proposed**

**Context**: separate admin web app; table/form-heavy; Arabic + English + RTL. Blocks Stage 1 and Milestone 6.

**Options**:
- **A — Flutter Web**: reuses tokens; tables feel awkward.
- **B — Next.js (React)** + component library (shadcn/ui, Mantine, Radix, Refine): strong admin ecosystem.
- **C — React + Vite** (no Next.js): lighter; no SSR needed behind login.
- **D — Blazor (Server/WASM)**: shares C#; smaller admin-component ecosystem.

**Decision**: _TBD_.

---

### ADR-007 · Payment providers · **Proposed**

**Context**: Apple Pay, Visa, MasterCard, Mada, STC Pay, bank transfer, COD, BNPL (Principle 13). Per-market selection. Blocks Stage 7 and Milestone 8.

**Shape of decision**: one primary gateway per market + one backup. BNPL is usually layered via a separate provider.

**Options — Egypt**: Paymob · Fawry · Accept · Kashier · MyFatoorah.
**Options — KSA**: HyperPay · Tap Payments · Moyasar · Checkout.com · PayTabs · MyFatoorah.
**Options — BNPL**: Tabby / Tamara (KSA); Valu / ContactNow (Egypt).
**Cross-market aggregators**: Checkout.com · MyFatoorah.

**Decision**: _TBD_.

---

### ADR-008 · Shipping providers · **Proposed**

**Context**: rate calculation, shipment creation, tracking, zones, delivery estimates, provider replacement (Principle 14). Blocks module 6.9 and Milestone 8.

**Options — Egypt**: Bosta · Aramex · Mylerz · R2S · Fetchr · J&T Express EG.
**Options — KSA**: SMSA · Aramex · SPL · DHL · J&T Express KSA · Naqel.
**Regional aggregators**: Shipox · Flixpro.

**Decision**: _TBD_ (recommendation: one primary + one backup per market; aggregator layer optional later).

---

### ADR-009 · Notification & OTP providers · **Proposed**

**Context**: push, email, SMS/WhatsApp across OTP, order updates, offers, abandoned cart, restock, price drop, verification, refunds, shipping (Principle 19). Blocks module 6.7 and Milestone 8.

**Options**:
- **Email**: Amazon SES · SendGrid · Postmark · Mailgun · Resend.
- **SMS / WhatsApp**: Twilio · Unifonic (strong KSA) · MessageBird · Vonage · Infobip.
- **Push**: Firebase Cloud Messaging (FCM); OneSignal as a wrapper with campaigns.

**Decision**: _TBD_ (common stack: SES + Unifonic + FCM for KSA residency; Twilio common for multi-market).

---

### ADR-010 · Cloud & data residency · **Proposed** (BLOCKER)

**Context**: KSA PDPL imposes residency/processing rules for KSA-resident personal data. Egypt Law 151/2020 applies to Egyptian residents. This ADR blocks any production infrastructure provisioning and is a hard prerequisite for Stage 7 and launch.

**Dimensions to decide**:
1. **Primary cloud**: AWS · Azure · GCP · Oracle Cloud · STC Cloud.
2. **Region**: KSA-compliant zone for KSA data (AWS `me-central-1` UAE, `me-south-1` Bahrain; Azure Saudi Arabia Central; GCP regional status evolving).
3. **Data-split strategy**: single region with per-market logical partitioning · per-market region with separate schemas · per-market tenants.
4. **Backups & DR**: also in compliant regions.
5. **Payment gateway data flows**: PCI scope kept minimal via hosted fields; no PAN stored.
6. **Analytics / logs pipeline**: must not export PII to non-compliant regions.

**Options (high level)**:
- **A — Azure Saudi Arabia Central** for all data; Egypt served from same region with market-field split.
- **B — AWS me-central-1 (UAE)** with per-market database separation.
- **C — Dual-region**: KSA region for KSA tenant, EG-closest region for Egypt tenant.
- **D — Hybrid**: self-managed app + managed DB in compliant region.

**Decision**: _TBD_ (must be resolved before Stage 7 and before any production resource is provisioned).

---

## 8. Decomposition into Spec-Kit feature specs

Hybrid: Stages 0–2 collapse into **three foundation specs**; Stages 3–9 split per-module into ~**26 module specs**. Feature folders are created by `./.specify/scripts/bash/create-new-feature.sh "<title>"` and follow `specs/###-<kebab-title>/`.

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
- **Deliverables**: this plan; ten ADRs in Section 7; Definition of Done (Section 11); feature spec `001-governance-and-setup`.
- **Principles touched**: 22, 23, 28, 29, 30, 31.
- **Blocking ADRs**: none (this stage seeds them).
- **Exit criteria**: all ten ADRs created; DoD approved; feature spec 001 approved.
- **Arabic/RTL gate**: N/A (no UI).

### Stage 1 — Architecture & contracts · Phase 1

- **Goal**: lock architecture before coding depth grows.
- **Deliverables**:
  - ADRs 001–006 and 010 resolved to **Accepted**.
  - API design rules (envelope, error model, pagination, filtering, idempotency keys, versioning, webhook security).
  - Domain overview (module boundaries, shared kernel).
  - Permissions matrix v1 (roles × permissions, includes B2B approver/buyer distinction).
  - **State models** for all seven Principle-24 domains (verification, cart/checkout, payment, order, shipment, return/refund, quote) — each with valid states, triggers, allowed actors, failure handling, retry handling. These are **exit-blocking**, not drafts.
  - Feature spec `002-architecture-and-contracts`.
- **Principles touched**: 6, 17, 22, 23, 24, 25, 29, 30.
- **Blocking ADRs**: 001, 002, 003, 004, 005, 006, 010.
- **Exit criteria**: all seven state models merged; API rules merged; permissions matrix approved; ADRs 001–006 + 010 Accepted.
- **Arabic/RTL gate**: localization-key strategy decided (ICU format; Arabic treated as a primary, not a fallback, in EG/KSA builds).

### Stage 2 — Shared foundations · Phase 1

- **Goal**: reusable contracts and design tokens before feature duplication begins.
- **Deliverables**:
  - Shared contract library (enums, statuses, role/permission names, DTO envelope, paging, filtering, error model) consumable by both Flutter and .NET.
  - Design-system foundation: constitution palette as tokens; typography, spacing, icon rules; button/input/card/badge/modal variants; empty/loading/error/restricted patterns; RTL behavior rules.
  - Localization foundation: Arabic + English key structure; locale-aware formatting; fallback behavior.
  - Feature spec `003-shared-foundations`.
- **Principles touched**: 4, 7, 24, 25, 27, 28.
- **Blocking ADRs**: resolutions from Stage 1.
- **Exit criteria**: any new module can consume shared contracts + tokens without copying code; palette matches constitution exactly.
- **Arabic/RTL gate**: Arabic strings render correctly in both apps at token level.

### Stage 3 — Backend core commerce domains · Phase 1

Goal: end-to-end browse-to-order in a controlled test environment. Each section is its own feature spec.

- **3.1 Identity & access**: registration, login, password auth, phone OTP, password reset, profile creation, sessions, role framework, customer vs admin separation. *Principles 3, 9, 24.*
- **3.2 Catalog**: categories, brands, products, media, documents, attributes/specs, restriction metadata, rich content fields, active/inactive states. *Principles 8, 10, 21.*
- **3.3 Search**: keyword, category/brand/offer/stock filters, sort, autocomplete, SKU/barcode, Arabic normalization. Blocked by ADR-005. *Principles 12, 26.*
- **3.4 Pricing & tax**: base / compare-at / discount price; market-aware resolution; coupon structure; promotion-engine skeleton; **VAT/tax computation for EG and KSA folded in here**. *Principles 10, 18.*
- **3.5 Inventory**: stock quantities; warehouse-ready design; available-to-sell; reservation rules; low-stock; batch/lot fields; expiry-ready model. *Principle 11.*
- **3.6 Cart**: guest cart, logged-in cart, merge, coupon application, validation. *Principles 3, 24.*
  - *"Save for later"* is explicitly **Phase 1.5** — out of V1.
- **3.7 Checkout**: address, shipping, billing, payment initiation, order preview, validation, restricted-product enforcement, stock revalidation; **invoice linkage defined here**. *Principles 8, 13, 18, 24.*
- **3.8 Orders**: placement, items, status history, payment status, fulfillment status, refund/return state (separate field), invoice link, reorder basics. Four orthogonal status fields — never merged. *Principles 17, 24, 25.*

**Blocking ADRs**: 001–006.
**Exit criteria**: anonymous user browses → registers → adds items → checks out (stub payment) → views order and downloads invoice.
**Arabic/RTL gate**: every API response carries localized display strings or localization keys; responses smoke-tested in Arabic.

### Stage 4 — Customer app core flows · Phase 1

- **Goal**: production-quality customer app shell for mobile + web storefront.
- **Scope**: single feature spec `014-customer-app-shell` covering shell, auth, home, listing, detail, cart, checkout, orders, more-menu. Vague items resolved:
  - *"Brand pages if included"* → **Phase 1** (brand-filtered listing; no dedicated brand page).
  - *"Recommended modules placeholders"* on Home → **Phase 1.5**. V1 Home = best-sellers + featured categories + banners only.
  - *"Trust indicators"* → **Phase 1**, static content (payment badges, licensed-clinic, verified-seller label) — no dynamic logic.
- **Principles touched**: 3, 4, 7, 8, 27.
- **Blocking ADRs**: 002.
- **Exit criteria**: every Phase 1 flow reachable from cold start on Android, iOS, web; loading/empty/error/success/restricted states verified on each.
- **Arabic/RTL gate**: full-screen audit in Arabic with RTL mirroring on every route.

### Stage 5 — Admin core operations · Phase 1

- **Modules**: 5.1 admin foundation · 5.2 catalog · 5.3 inventory · 5.4 orders · 5.5 customers.
- **Principles touched**: 20, 21, 25, 26, 27.
- **Blocking ADRs**: 006.
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
  - 6.7 Notifications (template management, event-triggered messages, campaign basics, preference management).
  - 6.8 Finance & invoices (tax invoice generation, invoice download, exportable finance views, refund record visibility).
  - 6.9 Shipping settings (provider settings, market rules, delivery methods, fee configuration, shipment state mapping).
  - **6.10 Returns & refunds (NEW)** — submission, eligibility check, admin decision flow, refund execution, audit, full state model.
- **Principles touched**: 8, 9, 15, 16, 17, 18, 19, 20, 21, 24, 25.
- **Blocking ADRs**: 007, 008, 009.
- **Exit criteria**: every module has spec + backend + admin UI + customer-facing surface where applicable.
- **Arabic/RTL gate**: every customer-visible notification template and PDF invoice localized and RTL-mirrored.

### Stage 7 — Integrations · Phase 1

- **Goal**: replace stubs with real provider-backed flows in staging.
- **Integration order**: OTP/SMS → email → push → payments → shipping → storage → PDF generation.
- **Principles touched**: 11, 13, 14, 18, 19.
- **Blocking ADRs**: 007, 008, 009, 010 (residency drives provider region).
- **Exit criteria**: staging runs every critical flow against real providers; no hardcoded provider references outside abstraction layers.
- **Arabic/RTL gate**: localized SMS/email templates verified in Arabic with correct number/date/currency formatting for EG and KSA.

### Stage 8 — Analytics, audit & monitoring · Phase 1.5 (not launch-blocking)

- **Scope**: event tracking, conversion funnel, search analytics, order/quote/verification/support metrics; advanced observability dashboards; payment-failure alerts; integration error alerts; uptime checks; structured logs.
- **Principles touched**: 25, 28.
- **Note**: audit logging itself (Principle 25) is Phase 1 and built inline with each module in Stages 3–6. What ships in Phase 1.5 is the **analytics dashboards and advanced observability**. Minimum Phase 1 = structured logs + basic uptime.

### Stage 9 — QA & hardening · Phase 1

- **QA tracks**: functional · localization · security · reliability · performance.
- **Exit criteria**: launch-readiness checklist (Section 13) passes.
- **Arabic/RTL gate**: full regression in Arabic.

---

## 10. Milestones

Nine milestones, each ~2–3 weeks with AI assistance. Every milestone must exit with an Arabic/RTL smoke test.

| # | Milestone | Covers | Approx duration |
|---|-----------|--------|-----------------|
| 1 | Foundation | Stages 0–2; specs 001–003; ADRs 001–006 + 010 resolved | 3 weeks |
| 2 | Identity + catalog + search | 3.1–3.3; specs 004–006 | 3 weeks |
| 3 | Pricing + inventory + cart | 3.4–3.6; specs 007–009 | 2 weeks |
| 4 | Checkout + orders + tax/invoice + returns | 3.7, 3.8, 6.8, 6.10; specs 010–013 | 3 weeks |
| 5 | Customer app shell | Stage 4; spec 014 | 3 weeks |
| 6 | Admin foundation + core admin modules | Stage 5; specs 015–019 | 3 weeks |
| 7 | Verification + B2B + reviews + support + CMS | 6.1–6.6; specs 020–024 | 3 weeks |
| 8 | Notifications + shipping + payments integration | 6.7, 6.9, Stage 7; specs 025–027 | 3 weeks |
| 9 | QA, hardening, launch prep | Stage 9; spec 029; baseline Stage 8 observability | 2 weeks |

Total: ~25 focused weeks. Do not compress Milestones 1, 4, or 9.

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
- [ ] Tests added (unit + integration where scoped).
- [ ] Code review passed.

---

## 12. Risk register

| # | Risk | Mitigation |
|---|------|------------|
| 1 | AI agents drift from spec | Constitution supremacy (Principle 31); ADRs; `/speckit-clarify`/`/speckit-analyze` gates before `/speckit-implement`. |
| 2 | Frontend invents backend behavior | Lane A leads; contracts shipped before UI; view models consume only typed contracts from the shared package. |
| 3 | Payment/order race conditions | Idempotency keys on every mutating endpoint; reservation rules at checkout; webhook replay discipline; reconciliation job. |
| 4 | Arabic support treated late | RTL/Arabic gate at every milestone exit; shared tokens enforce RTL from Milestone 1. |
| 5 | Admin underbuilt | Admin is Milestone 6 (before launch prep). No launch without Stage 5 exit met. |
| 6 | **B2B scope quietly reduced post-lock** | Constitution Principle 9 makes B2B mandatory V1. Any reduction requires a constitution amendment (Principle 32) — not a silent decision. |
| 7 | **Data residency breach at production cutover** | ADR-010 must be Accepted before any production resource is provisioned. Region locked to a KSA/EG-compliant zone. |
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
- [ ] Backup and restore verified.
- [ ] Secrets managed (no secrets in repo).
- [ ] Rate limits configured on public endpoints.
- [ ] ADRs 001–010 all **Accepted**.

### Integrations
- [ ] Payment provider live-ready per market.
- [ ] Shipping provider tested per market.
- [ ] OTP/SMS delivered to test numbers in EG and KSA.
- [ ] Email delivered with correct Arabic rendering.
- [ ] Push verified on Android + iOS.
- [ ] PDF invoices correct in Arabic and English.

### Operations
- [ ] Catalog loaded.
- [ ] Support team trained.
- [ ] Admin roles assigned per permissions matrix.
- [ ] Verification SOP ready.
- [ ] Refund SOP ready.
- [ ] Order-ops SOP ready.

### QA
- [ ] Full regression passed.
- [ ] Arabic editorial QA passed by a human reviewer (not MT-checked).
- [ ] Web + mobile smoke tests passed.
- [ ] Admin permissions matrix tested.

### Compliance
- [ ] KSA PDPL checks passed (residency + privacy notices).
- [ ] Egypt VAT invoice format verified with an accountant.
- [ ] Legal pages in Arabic + English reviewed.

### Monitoring
- [ ] Uptime monitor live.
- [ ] Error tracking active.
- [ ] Structured logs accessible.
- [ ] Payment-failure alerts firing.

---

## 14. Next actions

In this exact order:

1. Approve this plan.
2. Resolve **ADR-001** (monorepo layout). No code scaffolding starts before this.
3. Author feature spec **`001-governance-and-setup`** via `/speckit-specify`.
4. Author feature spec **`002-architecture-and-contracts`** — this is where state models and permissions matrix get authored and ADRs 002–006 + 010 get resolved.
5. Author feature spec **`003-shared-foundations`** — shared contract library, design tokens, localization scaffolding.
6. Only then begin Milestone 2 (identity + catalog + search) with per-module feature specs.

---

## 15. Appendix — disposition of the original ChatGPT plan

The ChatGPT "AI-Build Execution Plan" had useful structure but material gaps. This appendix documents how each issue was handled in this consolidated plan.

| Original item | Disposition |
|---------------|-------------|
| Proposed location `specs/11_delivery_plan/11_00_implementation_plan.md` | Moved to `docs/implementation-plan.md` to avoid Spec-Kit's `###-feature-name/` numbering collision. |
| Returns/refunds only in launch-readiness and QA | Added as module **6.10** with its own state model. |
| State machines listed as "drafts" in Stage 1 | Promoted to **blocking exit gate** at end of Stage 1 for all seven Principle-24 domains. |
| Tax/invoice only in Stage 6.8 | Folded into **Stage 3.4 (pricing)** and **Stage 3.7 (checkout)** as well. |
| Risk 6 (B2B may slip) | Rewritten as Risk 6 "B2B scope quietly reduced post-lock" — mitigation is constitutional amendment, not quiet rescope. |
| Phase labels missing on most scope items | Every scope item now carries a phase label (1 / 1.5 / 2). |
| No Arabic-first enforcement | Arabic/RTL gate at every milestone exit and every stage exit. |
| No data-residency gate | Added **ADR-010** as a blocker for Stage 7 and production provisioning. |
| Multi-vendor-ready not enforced | Added as DoD checkbox on every module. |
| Six parallel workstreams | Collapsed to two serial lanes for solo-dev reality. |
| "Save for later later if needed" | Deferred to **Phase 1.5**; typo removed. |
| "Brand pages if included" | Included in **Phase 1** (brand-filtered listing; no dedicated page). |
| "Recommended modules placeholders" on Home | Deferred to **Phase 1.5**. |
| "Trust indicators" | Included in **Phase 1** as static content, no dynamic logic. |
| "Reporting and optimization" appearing only in Section 2 | Absorbed into **Stage 8 (Phase 1.5)**. |
| Admin stack unnamed | Captured as **ADR-006**. |
| Six milestones assuming parallel teams | Resized to **nine solo-dev milestones** of 2–3 weeks each. |
