# Tasks: Architecture and Contracts

**Input**: Design documents from `specs/002-architecture-and-contracts/`
**Prerequisites**: plan.md ✅ | spec.md ✅ | research.md ✅ | data-model.md ✅ | contracts/architecture-document-contract.md ✅ | quickstart.md ✅
**Depends on**: Spec 001 at DoD (repo guardrails, CI pipeline, CODEOWNERS live)

**Organization**: Tasks grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no pending dependencies)
- **[Story]**: Which user story this task belongs to (US1–US6 from spec.md)
- Exact file paths are included in every task description

---

## Phase 1: Setup

**Purpose**: Directory skeleton and tooling baseline — no user-story work begins until complete.

- [X] T001 Create `docs/architecture/` directory with `.gitkeep` placeholder
- [X] T002 Create `docs/architecture/state-machines/` directory with `.gitkeep` placeholder
- [X] T003 [P] Create stub file `docs/architecture/erd.md` with only the version header: `**ERD Version**: 0.0.0 | **Date**: 2026-04-19 | **Status**: Stub`
- [X] T004 [P] Create stub file `docs/architecture/erd.dbml` with only a comment header: `// ERD DBML — dental commerce platform | Version: 0.0.0`
- [X] T005 [P] Create stub file `docs/architecture/permissions-matrix.md` with only the version header
- [X] T006 [P] Create stub file `docs/architecture/testing-strategy.md` with only the version header
- [X] T007 [P] Create stub files for all 7 state-machine domains with version headers: `docs/architecture/state-machines/verification.md`, `cart.md`, `payment.md`, `order.md`, `shipment.md`, `return.md`, `quote.md`
- [X] T008 Add `mmdc` (Mermaid CLI) as a dev dependency in a root-level `package.json` (or `docs/package.json` if preferred); confirm `npx mmdc --version` exits 0 in the CI environment

**Checkpoint**: `ls docs/architecture/` shows all stub files. `npx mmdc --version` exits 0.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Prerequisites shared by multiple user stories. MUST be complete before US1–US6.

**⚠️ CRITICAL**: No user story work begins until this phase is complete.

- [X] T009 Create `docs/architecture/index.md` stub with the section headings: `## Entity-Relationship Model`, `## State Machines`, `## Permissions Matrix`, `## Testing Strategy` — each with a placeholder `[link — pending]` row
- [X] T010 Add the `validate-diagrams` job to `.github/workflows/build-and-test.yml`: step that runs `npx mmdc --input <file> --output /dev/null` for every `.md` file touched in the PR; job name must be `validate-diagrams`; any non-zero exit blocks merge

**Checkpoint**: Open a PR with a deliberately invalid Mermaid block (e.g., ` ```mermaid\ninvalid ` ); confirm `validate-diagrams` fails and blocks merge. Fix it; confirm job passes.

---

## Phase 3: User Story 1 — Authoritative ERD (Priority: P1) 🎯 MVP

**Goal**: A single ratified Mermaid `erDiagram` covering all 19 Phase-1 domains — so every domain spec can find its entities, attributes, keys, and relationships without invention.

**Independent Test**: Open `docs/architecture/erd.md` on GitHub. Pick any of the 19 domains. Confirm its primary entities, attributes, foreign keys, and domain relationships are present. Confirm every ownable entity has a `vendor_id UUID NULL` FK. Run `npx mmdc --input docs/architecture/erd.md --output /dev/null` — exits 0.

- [X] T011 [US1] Author `docs/architecture/erd.md`: write the Mermaid `erDiagram` block for the **Identity & Access** domain — entities: `User`, `Role`, `Permission`, `UserRole`, `Session`, `OtpCode`, `PasswordResetToken`. Include PKs, FKs, nullable `vendor_id` where applicable. Set ERD Version: 1.0.0.
- [X] T012 [P] [US1] Extend `docs/architecture/erd.md`: add **Catalog** domain entities — `Category`, `Brand`, `Product`, `ProductVariant`, `ProductMedia`, `ProductDocument`, `ProductAttribute`, `ProductAttributeValue`. Include `vendor_id UUID NULL` FK on `Product`.
- [X] T013 [P] [US1] Extend `docs/architecture/erd.md`: add **Inventory** domain entities — `StockLocation`, `StockLedgerEntry`, `StockReservation`, `BatchLot`. Include `vendor_id UUID NULL` FK on `StockLocation`.
- [X] T014 [P] [US1] Extend `docs/architecture/erd.md`: add **Pricing & Tax** domain entities — `PriceList`, `PriceListEntry`, `TierPricingRule`, `BusinessPricing`, `Coupon`, `Promotion`, `PromotionRule`, `TaxRate`, `TaxProfile`.
- [X] T015 [P] [US1] Extend `docs/architecture/erd.md`: add **Cart & Checkout** domain entities — `Cart`, `CartItem`, `CartCouponApplication`, `CheckoutSession`, `Address`.
- [X] T016 [P] [US1] Extend `docs/architecture/erd.md`: add **Orders & Fulfillment** domain entities — `Order`, `OrderItem`, `OrderStatusHistory`, `Invoice`, `InvoiceLineItem`. Note: Order has four orthogonal status fields (Principle 17): `order_status`, `payment_status`, `fulfillment_status`, `return_status`.
- [X] T017 [P] [US1] Extend `docs/architecture/erd.md`: add **Returns & Refunds** domain entities — `ReturnRequest`, `ReturnItem`, `RefundTransaction`.
- [X] T018 [P] [US1] Extend `docs/architecture/erd.md`: add **Verification** domain entity — `VerificationApplication`, `VerificationDocument`.
- [X] T019 [P] [US1] Extend `docs/architecture/erd.md`: add **Quotes & B2B** domain entities — `Company`, `CompanyBranch`, `CompanyMember`, `Quote`, `QuoteItem`, `QuoteRevision`.
- [X] T020 [P] [US1] Extend `docs/architecture/erd.md`: add **Payments** domain entities — `PaymentIntent`, `PaymentAttempt`, `PaymentWebhookEvent`, `ReconciliationEntry`.
- [X] T021 [P] [US1] Extend `docs/architecture/erd.md`: add **Shipping** domain entities — `ShippingMethod`, `ShippingZone`, `Shipment`, `ShipmentTrackingEvent`.
- [X] T022 [P] [US1] Extend `docs/architecture/erd.md`: add **Notifications** domain entities — `NotificationTemplate`, `NotificationEvent`, `NotificationDeliveryLog`, `ChannelPreference`.
- [X] T023 [P] [US1] Extend `docs/architecture/erd.md`: add **CMS** domain entities — `Banner`, `FeaturedSection`, `FeaturedSectionItem`, `BlogPost`, `BlogCategory`, `LegalPage`, `FaqEntry`.
- [X] T024 [P] [US1] Extend `docs/architecture/erd.md`: add **Reviews** domain entity — `Review`, `ReviewMedia`.
- [X] T025 [P] [US1] Extend `docs/architecture/erd.md`: add **Support** domain entities — `SupportTicket`, `SupportTicketReply`, `SupportTicketAttachment`.
- [X] T026 [P] [US1] Extend `docs/architecture/erd.md`: add **Search** domain entity — `SearchSynonym` (index state is external; only config entities live in DB).
- [X] T027 [US1] Write `docs/architecture/erd.dbml`: transcribe the Mermaid ERD into DBML format — one table block per entity, fields with types, PKs, FKs, and references.
- [X] T028 [US1] Verify ERD completeness: confirm all 19 domains are present; every ownable entity has `vendor_id UUID NULL` FK; run `npx mmdc --input docs/architecture/erd.md --output /dev/null` and confirm exit 0; update `docs/architecture/index.md` ERD row with version 1.0.0.

**Checkpoint**: ERD renders on GitHub. `validate-diagrams` CI job passes on a PR containing the ERD. Every ownable entity has `vendor_id`. Index updated.

---

## Phase 4: User Story 2 — Seven State Machines (Priority: P1) 🎯

**Goal**: All 7 Principle-24 state machines diagrammed in Mermaid `stateDiagram-v2` with full transition tables — so domain specs cannot invent ambiguous statuses.

**Independent Test**: Open any state machine document. Run `npx mmdc --input docs/architecture/state-machines/<domain>.md --output /dev/null` — exits 0. Confirm: every minimum state from data-model.md is present; every transition row has Failure Behavior and Timeout Behavior; every authorized actor maps to a role in the permissions matrix.

All 7 state machine tasks are independent and can be authored in parallel.

- [X] T029 [P] [US2] Author `docs/architecture/state-machines/verification.md`: Mermaid `stateDiagram-v2` with states `draft → submitted → in_review → approved / rejected / info_requested`; expiry transition from `approved → expired`; Markdown table (From, To, Trigger, Authorized Actor, Failure Behavior, Timeout Behavior). Version: 1.0.0.
- [X] T030 [P] [US2] Author `docs/architecture/state-machines/cart.md`: states `active → merged / abandoned / converted_to_order`; soft-hold TTL shown on `active → abandoned` timeout path; Markdown table. Version: 1.0.0.
- [X] T031 [P] [US2] Author `docs/architecture/state-machines/payment.md`: states `pending → authorizing → authorized → captured`; failure paths to `failed`; refund path `captured → refunded / partially_refunded`; `voided` from `authorized`; Markdown table. Version: 1.0.0.
- [X] T032 [P] [US2] Author `docs/architecture/state-machines/order.md`: states `placed → confirmed → processing → shipped → delivered`; cancellation paths; `on_hold` side-state; Markdown table. Note: this document governs the `order_status` field only — the other three Order status fields (`payment_status`, `fulfillment_status`, `return_status`) are governed by payment, shipment, and return state machines respectively.
- [X] T033 [P] [US2] Author `docs/architecture/state-machines/shipment.md`: states `pending → created → picked_up → in_transit → out_for_delivery → delivered`; failure paths `failed_delivery → returned`; Markdown table. Version: 1.0.0.
- [X] T034 [P] [US2] Author `docs/architecture/state-machines/return.md`: states `requested → approved / rejected → items_received → refund_initiated → completed`; cancellation path; Markdown table. Version: 1.0.0.
- [X] T035 [P] [US2] Author `docs/architecture/state-machines/quote.md`: states `draft → submitted → under_review → revised / accepted / rejected`; expiry path; `converted_to_order` from `accepted`; Markdown table. Version: 1.0.0.
- [X] T036 [US2] Verify all 7 state machines: run `npx mmdc` against each; confirm all minimum states present; update `docs/architecture/index.md` state-machines section with version 1.0.0 for each document.

**Checkpoint**: All 7 `validate-diagrams` CI checks pass. Index updated with all 7 versions.

---

## Phase 5: User Story 3 — Permissions Matrix (Priority: P1) 🎯

**Goal**: A single permissions matrix covering 10 roles × all Phase-1 domain resources and actions, answerable in one lookup.

**Independent Test**: Pick any 10 (role, resource, action) triples from any Phase-1 domain spec description. Open `docs/architecture/permissions-matrix.md`. Confirm the answer (✅/❌/⚠️) is present for each in under 30 seconds.

- [X] T037 [US3] Author `docs/architecture/permissions-matrix.md` header section: version 1.0.0, role legend table (G, C, P, BB, BA, BrA, CO, AR, AW, AS with full role names), cell encoding key (✅ / ❌ / ⚠️ `[condition]`).
- [X] T038 [P] [US3] Add permissions-matrix section **Identity & Access**: rows for `register`, `login`, `view own profile`, `edit own profile`, `view any profile`, `manage roles`, `manage permissions`; cells for all 10 roles; footnotes for any ⚠️ conditions.
- [X] T039 [P] [US3] Add permissions-matrix section **Catalog**: rows for `browse products`, `view restricted product`, `purchase restricted product`, `create product`, `edit product`, `delete product`, `manage categories`, `manage brands`; cells for all 10 roles.
- [X] T040 [P] [US3] Add permissions-matrix section **Inventory**: rows for `view stock levels`, `adjust stock`, `view reservations`, `release reservations`, `manage batch/lot`; cells for all 10 roles.
- [X] T041 [P] [US3] Add permissions-matrix section **Cart & Checkout**: rows for `add to cart`, `view cart`, `apply coupon`, `initiate checkout`, `place order`; cells for all 10 roles.
- [X] T042 [P] [US3] Add permissions-matrix section **Orders**: rows for `view own orders`, `view any order`, `update order status`, `cancel order`, `initiate return`, `download invoice`; cells for all 10 roles.
- [X] T043 [P] [US3] Add permissions-matrix section **Pricing & Promotions**: rows for `view prices`, `view business pricing`, `create coupon`, `create promotion`, `set tier pricing`, `set business pricing`; cells for all 10 roles.
- [X] T044 [P] [US3] Add permissions-matrix section **Verification**: rows for `submit verification`, `view own verification`, `view any verification`, `review verification`, `approve/reject verification`; cells for all 10 roles.
- [X] T045 [P] [US3] Add permissions-matrix section **Quotes & B2B**: rows for `request quote`, `view own quotes`, `view company quotes`, `author quote`, `approve quote`, `convert quote to order`, `manage company members`; cells for all 10 roles.
- [X] T046 [P] [US3] Add permissions-matrix section **Reviews, Support, CMS, Notifications**: rows for `submit review`, `moderate review`, `create ticket`, `view tickets`, `reply to ticket`, `publish CMS content`, `manage notification templates`; cells for all 10 roles.
- [X] T047 [US3] Add permissions-matrix footnotes section resolving all ⚠️ conditions to single-sentence rules; update `docs/architecture/index.md` permissions-matrix row with version 1.0.0.

**Checkpoint**: Every domain section is present. Every ⚠️ cell has a footnote. Index updated.

---

## Phase 6: User Story 4 — Testing Strategy (Priority: P2)

**Goal**: A single testing strategy document defining required layers and mandatory scenario types per spec category — identical review bar for all 29 specs.

**Independent Test**: Read `docs/architecture/testing-strategy.md`. For each of the 5 spec categories, determine required layers and coverage posture in under 30 seconds. Confirm no numeric percentage appears anywhere in the document.

- [X] T048 [US4] Author `docs/architecture/testing-strategy.md` header and preamble: version 1.0.0, purpose statement, reference to `docs/dod.md` (the four universal scenario types are DoD requirements, not optional).
- [X] T049 [P] [US4] Add testing-strategy section **Backend domain spec**: required layers — Unit (MediatR handler + domain service), Integration (Testcontainers + PostgreSQL, full request pipeline), Contract (oasdiff on every PR); mandatory scenario types — every state transition, every error branch, every permission boundary, every acceptance scenario in the spec.
- [X] T050 [P] [US4] Add testing-strategy section **Flutter customer-app spec**: required layers — Widget test (per screen component), Integration test (flutter_test end-to-end flow), RTL golden test (one per screen in Arabic locale); mandatory scenario types — every acceptance scenario, every loading/empty/error UI state, every RTL layout.
- [X] T051 [P] [US4] Add testing-strategy section **Next.js admin spec**: required layers — Jest unit (per component and hook), Playwright E2E (every critical admin workflow); mandatory scenario types — every acceptance scenario, every permission-gated route, every form validation state.
- [X] T052 [P] [US4] Add testing-strategy section **Integration adapter spec**: required layers — Unit (mock adapter implementation), Integration (against provider sandbox or recorded cassette), Contract (schema diff); mandatory scenario types — every happy-path flow, every provider error response, every webhook replay.
- [X] T053 [P] [US4] Add testing-strategy section **Shared-contract spec**: required layer — contract diff via oasdiff on every PR; no separate test suite; any client-breaking diff fails the build.
- [X] T054 [US4] Verify testing-strategy: confirm 5 sections present; confirm the four universal scenario types are stated; grep for `%` in a coverage context and confirm zero matches; update `docs/architecture/index.md` testing-strategy row with version 1.0.0.

**Checkpoint**: All 5 spec-category sections present. No numeric thresholds. Index updated.

---

## Phase 7: User Story 5 — CI/CD Pipeline Ready for Domain Specs (Priority: P2)

**Goal**: The CI pipeline extended with Mermaid diagram validation so domain specs inherit a working validation pipeline immediately.

**Independent Test**: Open a PR with a malformed Mermaid block in any `.md` file. Confirm `validate-diagrams` fails and blocks merge. Fix the block and confirm the job passes.

- [X] T055 [US5] Verify and finalize `validate-diagrams` job scaffolded in Phase 2 T010: confirm the job correctly handles all 7 state-machine files and the ERD file; expand the path glob if T010's initial implementation only covers a subset of changed `.md` files; confirm `npm ci` installs Mermaid CLI and the job fails on a malformed block in any of the 9 architecture files.
- [X] T056 [US5] Add ERD amendment PR template addition to `.github/PULL_REQUEST_TEMPLATE/erd_amendment.md` (GitHub supports multiple PR templates via directory): template fields — ERD version before/after, entities changed, downstream specs affected, migration impact (breaking/additive/no-op).
- [ ] T057 [US5] Smoke-test the full CI pipeline against a sample PR that touches ERD and one state machine file: confirm `validate-diagrams`, `lint-format`, `verify-context-fingerprint`, and `build` all appear as required checks and pass.

**Checkpoint**: `validate-diagrams` job reliably blocks PRs with invalid Mermaid and passes PRs with valid Mermaid. ERD amendment PR template available.

---

## Phase 8: User Story 6 — ADR Finalization and Architecture Index (Priority: P2)

**Goal**: No ADR remains Proposed outside the Stage-7 deferral list. Architecture artifact index fully populated and discoverable in under 90 seconds.

**Independent Test**: Open `docs/implementation-plan.md` §7. Confirm: ADRs 001–006 and 010 each show `**Accepted**` with a concrete Decision line. ADRs 007, 008, 009 show `**Proposed**` with "Deferred to Stage 7" noted. Open `docs/architecture/index.md` and confirm all 11 artifacts linked with version numbers.

- [X] T058 [US6] Audit `docs/implementation-plan.md` §7: verify ADRs 001, 002, 003, 004, 005, 006, 010 each have status `**Accepted**` and a concrete, single-line Decision entry. For any that are missing or incomplete, add the Decision line per the approved decisions in the implementation plan.
- [X] T059 [P] [US6] Verify ADRs 007, 008, 009 in `docs/implementation-plan.md` §7 each have the note "Proposed — deferred to Stage 7 (provider selection at integration phase)" with the confirmed scope (007: BNPL both markets; 009: WhatsApp excluded from V1).
- [X] T060 [US6] Populate `docs/architecture/index.md` with final content: link to ERD (v1.0.0), link to all 7 state machines (each v1.0.0), link to permissions matrix (v1.0.0), link to testing strategy (v1.0.0); each row includes: artifact name, current version, last-amended date, one-sentence description, and link.

**Checkpoint**: ADR section has zero Proposed entries outside the three Stage-7 deferrals. `docs/architecture/index.md` renders all 11 links with versions. Time from fresh clone to finding the ERD via index: under 90 seconds.

---

## Phase 9: Polish and Cross-Cutting Concerns

- [X] T061 Run `quickstart.md` verification phases A–G in order; confirm every "Verify" step passes; document any failures before opening the PR
- [X] T062 [P] Verify `specs/002-architecture-and-contracts/spec.md` satisfies all Universal Core DoD items (UC-1 through UC-8); tick them in `checklists/requirements.md`
- [X] T063 [P] Confirm active applicability tags for this spec: no state-machine runtime (only documents), no audit events, no storage, no PDF, no user-facing strings — all tags inactive; note in PR template
- [ ] T064 Commit all deliverables on `002-architecture-and-contracts` branch; embed context fingerprint in PR description; open PR; ensure `validate-diagrams`, `lint-format`, `verify-context-fingerprint`, and `build` all pass

---

## Dependencies and Execution Order

### Phase dependencies

| Phase | Depends on | Blocks |
|---|---|---|
| Phase 1 (Setup) | Spec 001 at DoD | Phase 2 |
| Phase 2 (Foundational) | Phase 1 | All user story phases |
| Phase 3 (US1 — ERD) | Phase 2 | Phase 8 (index needs ERD version) |
| Phase 4 (US2 — State machines) | Phase 2 | Phase 8 (index needs SM versions) |
| Phase 5 (US3 — Permissions matrix) | Phase 2 | Phase 8 (index needs PM version) |
| Phase 6 (US4 — Testing strategy) | Phase 2 | Phase 8 (index needs TS version) |
| Phase 7 (US5 — CI extension) | Phase 2 | Phase 9 |
| Phase 8 (US6 — ADR + index) | Phases 3–6 | Phase 9 |
| Phase 9 (Polish) | Phases 3–8 | — |

### Parallel opportunities

- **Within Phase 1**: T003–T007 all parallel after T001 and T002.
- **Within Phase 3 (ERD)**: T012–T026 can all run in parallel once T011 sets the version header.
- **Within Phase 4 (State machines)**: T029–T035 are entirely independent — all 7 can be authored simultaneously.
- **Within Phase 5 (Permissions matrix)**: T038–T046 are independent domain sections — all can be authored simultaneously.
- **Within Phase 6 (Testing strategy)**: T049–T053 are independent sections — all can be authored simultaneously.
- **Within Phase 8**: T058 and T059 can run in parallel.

---

## Parallel Execution Example: Phase 4 (US2 — State Machines)

```
Once Phase 2 (T009, T010) is complete:

Parallel block — all 7 simultaneously:
  T029: docs/architecture/state-machines/verification.md
  T030: docs/architecture/state-machines/cart.md
  T031: docs/architecture/state-machines/payment.md
  T032: docs/architecture/state-machines/order.md
  T033: docs/architecture/state-machines/shipment.md
  T034: docs/architecture/state-machines/return.md
  T035: docs/architecture/state-machines/quote.md

Sequential after all 7 complete:
  T036: verify all 7 + update index
```

---

## Implementation Strategy

### MVP First (User Stories 1, 2, 3 — all P1)

1. Phase 1: Setup (T001–T008)
2. Phase 2: Foundational (T009–T010)
3. Phase 3: US1 — ERD (T011–T028)
4. Phase 4: US2 — State machines (T029–T036) — can overlap with Phase 3 domain sections
5. Phase 5: US3 — Permissions matrix (T037–T047)
6. **STOP and VALIDATE**: all three P1 artifacts are coherent and cross-referenced

### Full delivery (all six user stories)

1. MVP above
2. Phase 6: US4 — Testing strategy (T048–T054)
3. Phase 7: US5 — CI extension (T055–T057)
4. Phase 8: US6 — ADR finalization + index (T058–T060)
5. Phase 9: Polish (T061–T064)

---

## Notes

- `[P]` tasks write to different files and have no dependency on incomplete sibling tasks — all can run in parallel
- `[Story]` maps each task to a user story for traceability and independent testing
- No test-task layer generated: this spec produces documentation artifacts; CI validation (`mmdc`, `validate-diagrams`) serves as the automated quality gate
- State machine tasks (T029–T035) have the highest parallelism: all 7 can be written simultaneously with zero conflict
- ERD domain sections (T012–T026) can also be written in parallel; merge conflict risk is low since each touches a different entity cluster — resolve by section if conflict arises
- This spec has no active DoD applicability tags (no state-machine runtime, no audit events, no storage, no PDF, no user-facing strings)
