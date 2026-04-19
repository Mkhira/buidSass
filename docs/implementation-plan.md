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

From constitution Principle 30:

| Phase | Intent | Where in this plan |
|------|--------|--------------------|
| **Phase 1** | Strong launch-ready platform | Stages 0–9, Milestones 1–9 |
| **Phase 1.5** | Optimization and operational depth | "Deferred" notes inside each stage + full Analytics/Observability in Stage 8 |
| **Phase 2** | Marketplace / vendor expansion | Out of scope here; explicitly excluded: vendor portal, commissions, payouts, split checkout |

Every scope bullet in this doc is Phase 1 unless labeled otherwise.

```
 Phase 1 (launch)            Phase 1.5 (operate)         Phase 2 (expand)
 +----------------------+    +--------------------+      +----------------------+
 | Stages 0-9           | -> | Analytics dashes   |  ->  | Marketplace / vendor |
 | Milestones 1-9       |    | Advanced observab. |      | Payouts / split chk  |
 | Week 1 .. Week 25    |    | "Save for later"   |      | (out of scope here)  |
 +----------------------+    | Home recommenders  |      +----------------------+
                             | WhatsApp notif     |
                             +--------------------+
```

### 4.1 Phase scope inventory (spec-ready)

Every item below is a self-contained input for `/speckit-specify`. Copy the **Spec title** verbatim into the command; the **One-liner** is a starter description the spec command will expand. Phase labels map 1:1 to the constitution's Principle 30.

#### Phase 1 — Launch-blocking (29 specs)

Foundation (Milestone 1):

| # | Spec title | Phase | One-liner |
|---|------------|-------|-----------|
| 001 | governance-and-setup | 1 | Establish DoD, repo layout per ADR-001, CI skeleton, branch protection, CODEOWNERS, agent-context injection pattern, and the working cadence for all subsequent specs. |
| 002 | architecture-and-contracts | 1 | Lock API design rules, ERD, permissions matrix, seven Principle-24 state models, testing strategy, CI/CD bootstrap, and finalize remaining ADRs if any. |
| 003 | shared-foundations | 1 | Build shared contract library, design tokens, localization scaffolding, audit-log module, and storage + PDF abstractions consumed by every later module. |

Core commerce (Milestones 2-4):

| # | Spec title | Phase | One-liner |
|---|------------|-------|-----------|
| 004 | identity-and-access | 1 | Registration, login, phone OTP, password reset, sessions, role framework, customer/admin separation. |
| 005 | catalog | 1 | Categories, brands, products, media, documents, attributes, restriction metadata, active/inactive states. |
| 006 | search | 1 | Keyword + filter + sort + autocomplete + SKU/barcode + Arabic normalization behind a Meilisearch-backed service boundary. |
| 007-a | pricing-and-tax-engine | 1 | Price resolution pipeline, VAT/tax for EG and KSA, promotion engine primitives (stacking, exclusions). No UX here. |
| 008 | inventory | 1 | Stock, available-to-sell, soft-hold reservations with TTL, hard-commit on payment-auth, low-stock, batch/lot, expiry. |
| 009 | cart | 1 | Guest cart, logged-in cart, merge on login, coupon application, validation. |
| 010 | checkout | 1 | Address, shipping, billing, payment initiation, order preview, restricted-product enforcement, stock revalidation, invoice linkage. |
| 011 | orders | 1 | Placement, items, four orthogonal status fields, status history, invoice link, reorder basics. |
| 012 | tax-and-invoices | 1 | Tax invoice generation for EG/KSA, PDF output (Arabic + English), downloadable artifacts, finance export views. |
| 013 | returns-and-refunds | 1 | Return submission, eligibility check, admin decision flow, refund execution, full state model, audit. |

Customer app + admin (Milestones 5-6):

| # | Spec title | Phase | One-liner |
|---|------------|-------|-----------|
| 014 | customer-app-shell | 1 | Flutter (Bloc) UI for mobile + web: shell, auth, home, listing, detail, cart, checkout, orders, more-menu. UI-only; consumes contracts from 004-013. |
| 015 | admin-foundation | 1 | Next.js + shadcn/ui admin shell, auth, role-based navigation, layout, localization, audit-log reader. |
| 016 | admin-catalog | 1 | Category/brand/product CRUD, media + document upload, restriction metadata, bulk ops. |
| 017 | admin-inventory | 1 | Stock adjustments, low-stock queue, batch/lot management, expiry tracking, reservation inspection. |
| 018 | admin-orders | 1 | Order list + detail, status transitions, refund initiation, invoice reprint, B2B quote linkage. |
| 019 | admin-customers | 1 | Customer profile view, verification history, quote history, support-ticket linkage, address book view. |

Business modules (Milestone 7):

| # | Spec title | Phase | One-liner |
|---|------------|-------|-----------|
| 020 | verification | 1 | Professional verification submission, document upload, admin review queue, approve/reject/request-info, expiry, audit. |
| 021 | quotes-and-b2b | 1 | Quote requests, admin quote creation, revisions, acceptance, quote-to-order conversion, company accounts, PO numbers. |
| 007-b | promotions-ux-and-campaigns | 1 | Coupon lifecycle, scheduled promotions, banner-linked campaigns, business-pricing authoring, tier pricing. (Continues spec 007-a.) |
| 022 | reviews-moderation | 1 | Admin moderation queue, hide/delete, abuse notes, verified-buyer enforcement. |
| 023 | support-tickets | 1 | Ticket creation/list/detail, reply flow, category tagging, order-linked issues, SLA fields. |
| 024 | cms | 1 | Banners, featured sections, FAQ, legal pages, blog skeleton, localized publishing. |

Integrations (Milestone 8):

| # | Spec title | Phase | One-liner |
|---|------------|-------|-----------|
| 025 | notifications | 1 | Template management, event-triggered messages (SMS + email + push only at launch), campaign basics, preference management. |
| 026 | shipping | 1 | Provider settings, market rules, delivery methods, fee configuration, shipment state mapping, tracking webhooks. |
| 027 | payments-integration | 1 | Swap payment stubs for ADR-007 primary + backup per market; reconciliation job; webhook replay; BNPL wiring (Tabby/Tamara KSA + Valu EG). |

Launch (Milestone 9):

| # | Spec title | Phase | One-liner |
|---|------------|-------|-----------|
| 029 | qa-and-hardening | 1 | Functional + localization + security + reliability + performance regression; launch-readiness checklist (Section 13) passes. |

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

**Options — Egypt**: Bosta · Aramex · Mylerz · R2S · Fetchr · J&T Express EG.
**Options — KSA**: SMSA · Aramex · SPL · DHL · J&T Express KSA · Naqel.
**Regional aggregators**: Shipox · Flixpro.

**Decision**: _TBD in Stage 7_ (recommendation: one primary + one backup per market; aggregator layer optional later).

---

### ADR-009 · Notification & OTP providers · **Proposed (narrowed)**

**Context**: push, email, SMS across OTP, order updates, offers, abandoned cart, restock, price drop, verification, refunds, shipping (Principle 19). Decided in Stage 7 / Milestone 8.

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
