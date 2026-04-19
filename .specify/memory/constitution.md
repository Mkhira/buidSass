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
