# Claude Agent Context

<!-- context-fingerprint: 789f39325c0f0e8d7d646fc493718867540f9da41f1eed71c31bf15b53e8fb62 -->
<!-- context-fingerprint-source: .specify/memory/constitution.md + docs/implementation-plan.md §7 -->
<!-- generated-by: scripts/gen-agent-context.sh -->

## Constitution Principles (Verbatim)

<!--
SYNC IMPACT REPORT
==================
Version change: 0.0.0 (template) → 1.0.0
Bump rationale: MAJOR — first concrete population of all placeholder tokens; this is the initial ratified version.

Modified principles: N/A (no previous principles; all new)

Added sections (32 principles grouped into thematic sections):
  I.   Product & Market (Principles 1–7)
  II.  Commerce & Catalog (Principles 8–16)
  III. Order, Post-Purchase & Operations (Principles 17–21)
  IV.  Platform & Architecture (Principles 22–29)
  V.   Delivery & Governance (Principles 30–32)

Removed sections: None (initial population)

Templates checked:
  ✅ .specify/templates/plan-template.md — Constitution Check section present; no updates needed
  ✅ .specify/templates/spec-template.md — requirements model compatible; no updates needed
  ✅ .specify/templates/tasks-template.md — phasing model compatible; no updates needed
  ⚠  .specify/templates/commands/ — directory does not exist; no command templates to check

Deferred items: None — all placeholders resolved.
-->

# Dental Commerce Platform Constitution

## I. Product & Market

### 1. Product Mission
Build a modern, trusted, bilingual dental commerce platform for Egypt and KSA serving dentists,
clinics, dental labs, students, and general consumers. The platform launches as single-vendor but
MUST be architected as multi-vendor-ready for future expansion without a major rebuild.

### 2. Product Scope
This is NOT a minimal demo. Every launch feature MUST reflect real operational depth:
customer commerce, B2B workflows, admin operations, inventory depth, verification handling,
pricing and promotions, tax invoices, support flows, and analytics. Specs optimising for a
reduced demo product are non-compliant.

### 3. Experience Model
- Unauthenticated users MAY browse, search, view products, and view prices.
- Users MUST register or log in before checkout or payment.
- Specs MUST NOT assume consumer-only behavior; professional and B2B flows are first-class.

### 4. Market & Localization
Arabic and English MUST be fully supported across the mobile app, web storefront, admin
dashboard, invoices, notifications, and PDFs. All screens MUST support RTL layout, localized
dates/numbers/currency, and market-aware legal and tax presentation. Arabic quality MUST be
editorial-grade, not machine-translated. No screen or workflow MAY be English-only.

### 5. Market Configuration
Egypt and KSA MUST be treated as configurable markets. Market-specific behavior (currency,
VAT/tax rules, payment methods, COD eligibility, shipping methods, return policies, legal pages,
notification templates, verification fields) MUST be driven by market configuration — never by
hardcoded business logic scattered across the codebase.

### 6. Business Structure
The platform launches as single-vendor operationally and in UX. However, the backend, database,
permissions, catalog ownership, order models, and pricing structures MUST remain multi-vendor-ready.
Specs MUST NOT permanently hardcode single-vendor logic into product/inventory/fulfillment
ownership, future payout logic, future commission logic, or admin role structure. Future marketplace
complexity MUST NOT degrade launch UX.

### 7. Branding
Design MUST use the following palette as its base:
- Primary: `#1F6F5F`
- Secondary: `#2FA084`
- Accent: `#6FCF97`
- Neutral: `#EEEEEE`

Supporting semantic colors (success, warning, error, info) MAY be added for accessibility and
premium medical marketplace feel. No design spec MAY drift from this palette without explicit
design approval.

---

## II. Commerce & Catalog

### 8. Restricted Products
Some products require professional verification. Restricted products MAY remain visible and prices
MUST remain visible. Purchase eligibility MAY be restricted and approval MUST be handled by admin.
Eligibility MUST be enforced at both add-to-cart and checkout validation stages. All specs MUST
use a reusable restriction and eligibility model.

### 9. B2B
B2B is part of V1, not a future afterthought. All customer, cart, checkout, order, account, and
admin specs MUST support: quotation requests, approval flows, bulk ordering, repeat order templates,
company accounts, multi-user company accounts, buyer and approver roles, branch/company structure,
PO/reference numbers, and invoice billing. Any spec that ignores B2B requirements is incomplete.

### 10. Pricing
Pricing MUST support advanced commercial logic from V1: coupons, bundles, BOGO, tier pricing,
business pricing, scheduled promotions, and offers. Pricing logic MUST be centralized in a pricing
domain or service — not scattered across UI or ad hoc backend handlers. All totals MUST be
explainable and auditable.

### 11. Inventory
Inventory is NOT a simple stock count. All specs MUST support: stock tracking, warehouse readiness,
branch readiness, batch/lot numbers, expiry tracking, low-stock alerts, available-to-sell logic,
and reservation/revalidation during checkout. Inventory design MUST be compatible with future
multi-vendor and multi-warehouse expansion.

### 12. Search
Search is a core product capability from day one. All specs MUST support: autocomplete, synonym
search, Arabic normalization, SKU search, barcode search, typo tolerance, sorting, and faceted
filters. Search MUST be designed behind a service boundary so search technology can evolve without
rewriting product logic.

### 13. Payment
The platform MUST support Apple Pay, Visa, MasterCard, Mada, STC Pay, bank transfer, COD where
suitable, and BNPL where suitable. Payment integrations MUST go through a payment abstraction
layer. No spec MAY hardcode business logic to a single provider. Payment workflows MUST support
retries, failed payment recovery, pending states, reconciliation, and idempotency.

### 14. Shipping
Shipping MUST use a generic integration layer. No spec MAY tightly couple fulfillment logic to one
provider. Shipping architecture MUST support provider abstraction, shipment creation, tracking,
fee calculation, region/zone logic, delivery estimates, provider replacement, and future
multi-warehouse and marketplace expansion.

### 15. Reviews
Only verified buyers MAY submit reviews. Admins MUST be able to hide or delete reviews.
Moderation support is REQUIRED. Review data MUST be linked to completed-purchase eligibility.

### 16. CMS
Admin-managed CMS is part of V1. It MUST support homepage banners, featured sections, blog and
educational content, guides, FAQ, legal pages, and SEO pages. Customer-facing screens MUST be
designed to consume dynamic CMS-managed content where appropriate.

---

## III. Order, Post-Purchase & Operations

### 17. Order & Post-Purchase
Post-purchase experience MUST be strong. Required: order statuses, timeline/stepper, invoice
download, reorder, support shortcut, tracking, issue reporting, cancellation eligibility,
return/refund requests, and payment retry for failed payments. All order specs MUST separately
model: order state, payment state, fulfillment state, and refund/return state. Merging these
states into a single status field is non-compliant.

### 18. Tax & Invoice
Tax invoicing MUST be supported from day one for Egypt and KSA. Requirements: downloadable tax
invoices, proper VAT/tax handling, billing details, B2B invoice support, admin visibility, and
admin exportability. No checkout or order spec is complete without invoice and tax treatment.

### 19. Notifications
The system MUST support push, email, and SMS/WhatsApp. Notifications MUST cover: OTP, order
updates, offers, abandoned cart, restock, price drop, verification results, refunds, and shipping
updates. Admin-managed campaigns are REQUIRED. Notification architecture MUST support templates,
localization, event-triggered sends, channel preferences, and delivery logging.

### 20. Admin Dashboard
The admin dashboard MUST be a separate web application supporting Arabic and English. Minimum
modules: analytics, catalog, inventory, orders, customers, verification, promotions, reviews,
quotations, CMS, support, finance, shipping settings, notifications/campaigns, roles/permissions,
and audit logs. Any implementation delivering only a partial admin shell is incomplete.

### 21. Operational Readiness
The system MUST be designed for real operations from day one: catalog operations, inventory
operations, warehouse readiness, lot/batch handling, expiry tracking, low-stock alerts, order
management, returns/refunds, support/ticketing, campaign management, finance/admin controls,
audit logs, roles/permissions, shipping configuration, and reporting and analytics.

---

## IV. Platform & Architecture

### 22. Fixed Technology Decisions
These are LOCKED unless explicitly changed by product leadership:
- Frontend: Flutter (mobile app and web storefront)
- Backend: .NET
- Database: PostgreSQL
- Admin: Separate admin web application

No spec MAY propose replacing these core technologies without a formal architecture change approval.

### 23. Architecture
The default direction is a modular monolith backend with explicit domain boundaries, clean APIs,
and future service extraction where needed. The project MUST optimize for fast delivery,
maintainability, operational clarity, and AI-assisted implementation. Premature microservices
design is non-compliant unless explicitly approved.

### 24. State Machines
The following domains MUST use explicit state models: verification, cart/checkout, payment, order,
shipment, return/refund, and quote. Vague status handling is non-compliant. Each state model MUST
define: valid states, transition rules, triggers, actors allowed to transition, failure handling,
and retry handling.

### 25. Data & Audit
Critical actions MUST be auditable. Audit trails are REQUIRED for: admin actions, verification
decisions, refund decisions, order status changes, price changes, inventory changes, role changes,
permission changes, and CMS publishing. AI-generated implementation MUST preserve traceability
and accountability.

### 26. Search Architecture
(See Principle 12.) Search MUST be decoupled behind a service boundary enabling evolution of the
underlying search technology without rewriting product logic.

### 27. UX Quality
The experience MUST feel modern, marketplace-grade, premium medical, trustworthy, and operationally
clear. The UI MUST balance simplicity for consumers, efficiency for professionals, and density
where useful for B2B and admin. All design specs MUST include: loading states, empty states, error
states, success states, restricted-state messaging, payment-failure recovery, and accessibility
considerations.

### 28. AI-Build Standard
This project is implemented with help from Claude, Codex, GLM, and UI/UX AI tools. All specs
MUST be: explicit, structured, modular, low-ambiguity, implementation-ready, and
acceptance-criteria-driven. Vague language ("support this somehow", "nice modern UX", "standard
flow", "usual dashboard behavior") is non-compliant. Specs MUST define: roles, rules, states,
fields, APIs, validations, edge cases, and success criteria.

### 29. Required Spec Output Standard
Every spec MUST include (where relevant):
1. Goal
2. User roles
3. Business rules
4. User flow
5. UI states
6. Data model
7. Validation rules
8. API or service requirements
9. Edge cases
10. Acceptance criteria
11. Phase assignment
12. Dependencies

---

## V. Delivery & Governance

### 30. Phasing
Delivery MUST be structured in three phases:
- **Phase 1**: Strong launch-ready platform
- **Phase 1.5**: Optimization and operational depth
- **Phase 2**: Marketplace/vendor expansion

Every spec MUST clearly assign scope to one phase. Hidden scope creep is non-compliant.

### 31. Constitution Supremacy
If any feature spec, UI spec, API spec, or implementation task conflicts with this constitution,
this constitution takes precedence. The conflict MUST be explicitly flagged and the spec MUST be
revised before implementation proceeds.

### 32. Amendment Procedure
Amendments to this constitution MUST:
1. Be explicitly flagged as a proposed amendment.
2. Receive approval from product leadership (or a designated architecture decision record).
3. Increment the version according to semantic versioning: MAJOR for incompatible governance/
   principle removals or redefinitions; MINOR for new principles or materially expanded guidance;
   PATCH for clarifications, wording, or non-semantic refinements.
4. Update `LAST_AMENDED_DATE`.
5. Trigger a consistency propagation check across all spec and plan templates.

All PRs and reviews MUST verify compliance with the active version of this constitution before
merge.

---

## Governance

This constitution is the governing foundation for the dental commerce platform spec package.
All future documents MUST inherit from it and comply with it unless a later signed architecture or
product decision explicitly overrides a specific section.

**Version**: 1.0.0 | **Ratified**: 2026-04-19 | **Last Amended**: 2026-04-19

## ADR Decisions Table

| ADR | Title | Status | Decision |
|---|---|---|---|
| ADR-001 | Monorepo layout | Accepted | Single monorepo, no build tool. Layout: |
| ADR-002 | Flutter state management | Accepted | Bloc / flutter_bloc. Strict unidirectional flow, well-understood by AI agents, strong testability. More boilerplate than Riverpod but trade-off is worth it for AI-agent output consistency. |
| ADR-003 | .NET API style | Accepted | Vertical slice + MediatR. Handler-per-feature maps 1:1 onto Spec-Kit per-feature folders (\`specs/###-feature-name/\` ↔ \`Features/FeatureName/\` in the backend). Clean for AI-agent execution — each spec produces a cohesive slice instead of spreading code across layers. |
| ADR-004 | ORM & migrations | Accepted | EF Core 9, code-first migrations. Hybrid (EF Core + Dapper) deferred until profiling proves a hot path needs it. Soft-delete via query filters; audit hooks via \`SaveChangesInterceptor\`. |
| ADR-005 | Search engine | Accepted | Meilisearch. Good Arabic normalization out of the box, strong typo tolerance, simple ops, lowest friction for solo + AI-agent execution. Facet + filter API fits storefront needs. Hosted in the same Azure Saudi Arabia Central region as the DB (ADR-010). |
| ADR-006 | Admin web stack | Accepted | Next.js + shadcn/ui. Strong admin ecosystem (tables, forms, modals), largest AI training corpus (high agent-output quality), SSR not strictly needed behind login but App Router fits cleanly. Admin runs in Lane B under the GLM agent. |
| ADR-007 | Payment providers | Proposed | _TBD in Stage 7._ |
| ADR-008 | Shipping providers | Proposed | _TBD in Stage 7_ (recommendation: one primary + one backup per market; aggregator layer optional later). |
| ADR-009 | Notification & OTP providers | Proposed (narrowed) | _TBD in Stage 7_ (common stack: SES + Unifonic + FCM aligns with KSA residency and ADR-010). |
| ADR-010 | Cloud & data residency | Accepted | Azure Saudi Arabia Central for all tenants (KSA + EG), single-region with per-market logical partitioning via a \`market_code\` column on every tenant-owned entity. |

## Four Guardrails

1. Lint + format checks must pass on every PR.
2. Contract diff checks must pass on every PR.
3. Constitution + ADR fingerprint must be included and verified on PRs.
4. Constitution and ADR edits require protected human code-owner approval.

## How to work in this repo

- Respect all 32 constitution principles at all times.
- Principle 32 amendment procedure applies to all governance changes.
- Use the ADR decisions table as the default architectural baseline.
- Compute PR fingerprint with `scripts/compute-fingerprint.sh`.
- Apply DoD from `docs/dod.md` (DoD version: 1.0).
- Constitution version in source context: 1.0.0.

## Design-agent skills (D1 workstream)

Impeccable design skills are vendored under this repo at commit `00d485659af82982aef0328d0419c49a2716d123` (see `.impeccable/VERSION`, Apache-2.0).

**When working on a UI-bearing spec** — any spec numbered 014–024 or 029, OR any change touching `apps/customer_flutter/**`, `apps/admin_web/**`, or `packages/design_system/**`:

1. Load the `impeccable-brand` skill **before** any `impeccable` command (`/impeccable`, `/audit`, `/polish`, `/critique`, `/typeset`, `/colorize`, `/layout`, `/harden`, `/adapt`, `/animate`, `/delight`, `/quieter`, `/bolder`, `/distill`, `/clarify`, `/shape`, `/optimize`, `/overdrive`). The brand overlay encodes Principle 7 palette, Principle 4 Arabic/RTL editorial rules, and medical-marketplace tone; it overrides upstream impeccable defaults where they conflict.
2. Run `/audit` before opening a UI PR; attach the report to the PR description.
3. The impeccable CLI scanner runs as an **advisory** CI job on `apps/admin_web` PRs (see `.github/workflows/impeccable-scan.yml`); it is **promoted to merge-blocking in Phase 1F spec 029** per `docs/design-agent-skills.md`.

**Backend-only specs (001–013, 025–028) must not invoke impeccable.** The skills are not loaded there; reaching for them wastes context.

Upgrade procedure, CLI usage, and waiver path live in `docs/design-agent-skills.md`.

## Active Technologies
- C# 12 / .NET 9 (LTS), PostgreSQL 16 (004-identity-and-access)
- PostgreSQL (Azure Saudi Arabia Central per ADR-010). 17 new tables across identity / session / OTP / admin-provisioning / MFA / RBAC / rate-limit domains. Reuses spec 003's `audit_log_entries`. In-process bloom filter mirrors the refresh-token revocation set to cut DB chatter on the hot path. (004-identity-and-access)
- C# 12 / .NET 9 (LTS), PostgreSQL 16 (per spec 004 + ADR-022). (phase_1D_creating_specs)
- PostgreSQL (Azure Saudi Arabia Central per ADR-010). 5 new tables in the `verification` schema: (phase_1D_creating_specs)
- PostgreSQL (Azure Saudi Arabia Central per ADR-010). 10 new tables in the `b2b` schema: (phase_1D_creating_specs)
- PostgreSQL (Azure Saudi Arabia Central per ADR-010). 5 new tables + additive columns on 2 existing 007-a tables in the `pricing` schema for the 007-b commercial-authoring layer: `commercial_thresholds`, `campaigns`, `campaign_links`, `preview_profiles`, `commercial_approvals`, `commercial_audit_events` (append-only via trigger). Lifecycle columns added to `pricing.coupons` / `pricing.promotions`; `company_id` + state columns added to `pricing.product_tier_prices`. No engine change — Preview tool calls existing 007-a `IPriceCalculator.Calculate(ctx)` in Preview mode. (phase_1D_creating_specs)
- PostgreSQL (Azure Saudi Arabia Central per ADR-010). 9 new tables in the `support` schema for spec 023: `tickets`, `ticket_messages`, `ticket_attachments`, `ticket_links`, `ticket_assignments`, `ticket_sla_breach_events`, `sla_policies`, `support_market_schemas`, `agent_availability`. 5-state Ticket lifecycle (`open → in_progress ↔ waiting_customer → resolved → closed`); reopen edge `resolved → in_progress` within per-market window; frozen-at-creation SLA snapshot per ticket. (phase_1D_creating_specs)
- PostgreSQL (Azure Saudi Arabia Central per ADR-010). 9 new tables in the `cms` schema for spec 024: `banner_slots`, `featured_sections`, `faq_entries`, `blog_articles`, `legal_page_versions`, `assets`, `preview_tokens`, `banner_campaign_bindings`, `market_schemas`. Unified 4-state content lifecycle (`draft → scheduled → live → archived`) shared by 5 entity kinds; legal page versions add a `superseded` terminal state in place of `archived`. Per-market `CmsMarketSchema` policy holds banner-capacity cap, asset GC grace window, draft-staleness alert window, and preview-token TTL default. (phase_1D_creating_specs)

## Recent Changes
- 004-identity-and-access: Added C# 12 / .NET 9 (LTS), PostgreSQL 16
- 007-b-promotions-ux-and-campaigns: Authoring + lifecycle + preview + audit + campaign linkage layer over the existing 007-a `Pricing` module. Two state machines (`LifecycleState` 5-state, `BusinessPricingState` 2-state). High-impact approval gate (gate ON at launch, conservative seeded thresholds per market). Cross-module event ingestion (`catalog.sku.archived`, `b2b.company.suspended`); `BrokenReferenceAutoDeactivationWorker` daily with 7-day grace. In-flight grace contract carried in deactivation events for spec 010 checkout. Hard-delete forbidden on Coupons / Promotions / Campaigns (FR-005a).
- 022-reviews-moderation: New `Modules/Reviews/` vertical-slice module. 7 net-new tables in a `reviews` schema. 5-state Review lifecycle (`pending_moderation | visible → flagged → hidden ↔ visible | deleted`). Verified-buyer gate via spec 011's `IOrderLineDeliveryEligibilityQuery`. Single-locale review content (no auto-translation per Principle 4). Profanity filter (per-market wordlist; reuses spec 006's `IArabicNormalizer`) + media-attachment auto-hold for moderation. Community-report flow with qualified-reporter weighting (account-age + verified-buyer; per-market tunable). `RatingAggregateRecomputer` immediate-on-transition + daily reconciliation worker. Hard-delete forbidden (FR-005a).
- 023-support-tickets: New `Modules/Support/` vertical-slice module. 9 net-new tables in a `support` schema. 5-state Ticket lifecycle with reopen edge. Polymorphic `TicketLink` to `order/order_line/return_request/quote/review/verification` via per-module read contracts in `Modules/Shared/` (loose-coupling pattern from specs 020/021/022). Frozen-at-creation SLA snapshots per ticket (per-market + per-priority); `SlaBreachWatchWorker` runs every 60s idempotent on `(ticket_id, breach_kind)`. Bidirectional ticket↔return-request conversion (idempotent invocation of spec 013's `IReturnRequestCreationContract`; `return.completed` event auto-resolves originating ticket). Internal notes vs customer-visible replies separated by message kind; non-assigned-agent reply-and-transition forbidden, internal notes allowed. Minimal `SupportAgentAvailability.is_on_call` boolean per `(agent_id, market_code)` at V1; full shift planner deferred to Phase 1.5. PII redaction paths: super_admin attachment redaction (FR-012a) + customer-initiated message-body redaction-request flow auto-routed to super_admin queue (FR-011a). Hard-delete forbidden (FR-005a).
- 024-cms: New `Modules/Cms/` vertical-slice module. 9 net-new tables in a `cms` schema. Unified 4-state content lifecycle (`draft → scheduled → live → archived`) shared by all 5 entity kinds (banner slots, featured sections, FAQ entries, blog articles, legal page versions); legal page versions extend with a `superseded` terminal state. Locale-completeness gate at publish (`ar` + `en` mandatory for banner/featured/FAQ/legal; blog allows single-locale per Principle 4 long-form rule). Banner-slot capacity cap (`CmsMarketSchema.banner_max_live_per_slot`, V1 default 5; `*`-scoped banners count against every per-market cap). Banner CTA validation via `Modules/Shared/` catalog read contracts at BOTH publish-time and storefront-read-time (fail-open `cta_health=transient_unverified` on transient catalog errors). Featured-section refs polymorphic jsonb, live-resolved at storefront read; broken refs filtered. Two-tier storefront sort (specific market first, then `*`). Indefinite legal page version retention; `*`-scoped legal page publish requires `super_admin`. Reference-counted asset GC (`CmsAssetGarbageCollectorWorker` daily; 7-day per-market grace window; `CmsAsset` metadata preserved with `storage_object_state=swept`). Stale-draft soft alerting only — NEVER auto-archives (FR-034a). Signed HMAC-SHA256 preview tokens with bounded TTL + immediate revocation; daily token-cleanup worker. Banner ↔ 007-b campaign binding via append-only `BannerCampaignBinding`; auto-released on `pricing.campaign.deactivated`. Hard-delete forbidden on every non-`draft` row (FR-005a). 4 hosted workers; 21 domain events; 19 audit-event kinds.
