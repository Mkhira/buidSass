---

description: "Tasks for Spec 019 — Admin Customers"
---

# Tasks: Admin Customers

**Input**: Design documents from `/specs/phase-1C/019-admin-customers/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/{consumed-apis.md,routes.md,client-events.md}, quickstart.md

**Tests**: vitest + RTL unit/component, Playwright + Storybook visual regression (SC-003), axe-playwright a11y, Playwright e2e for Stories 1/2/3. PII-leak unit sweep (SC-007 enforcement) + no-403-after-render contract test (SC-004 enforcement). Inherits spec 015's CI / lint hygiene.

**Organization**: Tasks grouped by user story. Stories run in priority order P1 → P4.

## Format

`[ID] [P?] [Story] Description (path)`

- `[P]` — parallelizable
- `[USn]` — user story (US1 = P1 MVP)

## Path conventions

- App code: `apps/admin_web/`
- Customers feature lives under `apps/admin_web/app/(admin)/customers/`, `apps/admin_web/components/customers/`, `apps/admin_web/lib/customers/`

---

## Phase 1: Setup (customers-specific scaffolding)

- [ ] T001 No new runtime deps for 019 — verify `pnpm install` resolves cleanly. Every primitive is in the admin app already.
- [ ] T002 [P] Add the 6 customers permission keys (per `contracts/routes.md`) to spec 015's `apps/admin_web/lib/auth/permissions.ts` map.
- [ ] T003 [P] Add `apps/admin_web/lib/customers/.gitkeep` and the three feature dirs under `app/(admin)/customers/` (`page.tsx` placeholder, `[customerId]/`, `[customerId]/addresses/`, `[customerId]/company/`).
- [ ] T004 [P] Append customers feature flags to `apps/admin_web/lib/env.ts`: `flags.adminVerificationsShipped`, `flags.adminQuotesShipped`, `flags.adminSupportShipped` (`adminOrdersShipped` already shipped in spec 018's setup).

---

## Phase 2: Foundational (customers-specific shared infrastructure)

⚠️ Required before any user-story phase begins.

### Generated client + state machine + action gate

- [ ] T005 Extend `apps/admin_web/lib/api/clients/identity.ts` (from spec 015 / 018 baseline) wrapping every spec 004 endpoint used: customer list / search / detail / addresses / B2B-company-hierarchy / suspend / unlock / password-reset-trigger.
- [ ] T006 Create `apps/admin_web/lib/customers/action-gate.ts` exporting `evaluateAccountAction(...)` per research §R2 — the pure decision function consumed by every action button.
- [ ] T007 [P] Create `apps/admin_web/lib/customers/action-gate.test.ts` covering every (action × customerState × permission profile × isSelf) combination.
- [ ] T008 [P] Create `apps/admin_web/lib/customers/action-state.ts` (SM-1) per `data-model.md`.
- [ ] T009 [P] Create `apps/admin_web/lib/customers/feature-flags.ts` exporting the flag map per research §R4.

### Step-up dialog promotion

- [ ] T010 Verify `<StepUpDialog>` lives at `apps/admin_web/components/shell/step-up-dialog.tsx` (promoted from spec 018). If still under `components/orders/refund/`, file `spec-015:gap:step-up-dialog-promotion`, move the component, and update spec 018's import path in the same PR.

### PII redaction primitive

- [ ] T011 Create `apps/admin_web/components/customers/shared/masked-field.tsx` exporting `<MaskedField kind="email" | "phone" value={raw} canRead={hasPermission} />` per research §R1. Include localized "email hidden" / "phone hidden" copy + tooltip.
- [ ] T012 [P] Create `apps/admin_web/lib/customers/pii-mask.ts` — pure formatter `maskEmail(raw)` / `maskPhone(raw)` for use outside React (e.g., search-result list rendering).
- [ ] T013 [P] Create `apps/admin_web/components/customers/shared/masked-field.test.tsx` covering masked + unmasked + a11y label rendering.

### Sidebar contribution + i18n keys + nav-manifest

- [ ] T014 [P] Update spec 015's nav-manifest consumer (`apps/admin_web/lib/auth/nav-manifest.ts`) to render the **Customers** group when the admin's permission set includes `customers.read`; surface "Companies" sub-entry conditioned on `customers.b2b.read`.
- [ ] T015 [P] Append customers i18n keys to `apps/admin_web/messages/en.json` (page titles, filter labels, role labels, account-state labels, masked-field copy, action-confirmation dialogs, history-panel placeholder copy).
- [ ] T016 [P] Mirror to `apps/admin_web/messages/ar.json`. Editorial pass deferred to the cross-cutting AR/RTL phase.

### Customers shared composites

- [ ] T017 [P] Create `apps/admin_web/components/customers/shared/conflict-overlay.tsx` for the 412 stale-version flow on account actions.
- [ ] T018 [P] Create `apps/admin_web/components/customers/shared/feature-flagged-panel.tsx` per research §R4 wrapping the three history panels.
- [ ] T019 [P] Create `apps/admin_web/components/customers/shared/history-panel-placeholder.tsx` (shared placeholder shell for the three flag-gated panels).

### Customers overview

- [ ] T020 Create `apps/admin_web/app/(admin)/customers/layout.tsx` (sub-shell highlighting the customers sidebar group when inside `/customers/*`).

**Checkpoint**: foundation ready.

---

## Phase 3: User Story 1 — Find a customer and review profile (Priority: P1) 🎯 MVP

**Goal**: list → filter / search → open profile → identity / roles / addresses / orders summary.

### Customers list

- [ ] T021 [US1] Create `apps/admin_web/app/(admin)/customers/page.tsx` (Server Component renders DataTable + filter bar + initial cursor page).
- [ ] T022 [US1] Create `apps/admin_web/components/customers/list/customers-table.tsx` wrapping spec 015's `DataTable` (no checkbox column — bulk actions out of scope).
- [ ] T023 [US1] [P] Create `apps/admin_web/components/customers/list/filter-bar.tsx` — market single-select + B2B tristate + verification-state multi-select + account-state multi-select; URL-synced.
- [ ] T024 [US1] [P] Create `apps/admin_web/components/customers/list/search-bar.tsx` — server-side free-text via spec 004's customer-search endpoint, debounced 300 ms (R5).
- [ ] T025 [US1] [P] Wire `<MaskedField>` (spec 015 T040f — promoted to shell shared primitive) into the email / phone columns of `<CustomersTable>` so PII is consistently redacted (FR-007). Per FR-007a, `<MaskedField>` debounces emit `customers.pii.field.rendered` telemetry exactly once per row mount with `mode: 'masked' | 'unmasked'` + `kind: 'email' | 'phone'`.
- [ ] T025a [US1] [P] Per FR-007a, create `tests/unit/customers/telemetry/pii-mask.test.tsx` asserting (a) a permission-less render emits exactly one `customers.pii.field.rendered` per row with `mode: 'masked'`, (b) a permitted render emits with `mode: 'unmasked'`, (c) re-renders don't multiply emissions, (d) the event payload contains no value, no customer id, and no field name beyond `kind`. The unmasked/masked ratio is the regression signal operations watches.
- [ ] T026 [US1] [P] Create `apps/admin_web/app/api/customers/route.ts` (GET list + search) and `app/api/customers/search/route.ts` proxies.
- [ ] T027 [US1] [P] Create `tests/unit/customers/list/{customers-table,filter-bar,search-bar}.test.tsx`.
- [ ] T028 [US1] [P] Add Storybook stories for the list (empty / dense / saved-views applied / masked-PII / unmasked-PII) in EN-LTR + AR-RTL × {light, dark}.
- [ ] T029 [US1] [P] Create `tests/visual/customers/list.spec.ts`.

### Customer profile detail

- [ ] T030 [US1] Create `apps/admin_web/app/(admin)/customers/[customerId]/page.tsx` (Server Component composes identity + roles + addresses preview + orders summary + company card + history panels). Per FR-018a, mount `<AuditForResourceLink resourceType="Customer" resourceId={customerId} />` (spec 015 T040e) in the page header.
- [ ] T031 [US1] [P] Create `apps/admin_web/components/customers/profile/identity-card.tsx` — display name + masked email + masked phone + market + locale + account-creation + last-active.
- [ ] T031a [US1] [P] Per FR-008 + the post-third-pass Suspension card requirement, create `apps/admin_web/components/customers/profile/suspension-card.tsx` rendered only when `customer.accountState === 'suspended'`. Shows the most recent suspend reason note + actor + timestamp, sourced from spec 004's lockout-state record when published; until then, surfaces a "see audit log for reason" link that resolves via `<AuditForResourceLink>` filtered by `actionKey=customers.account.suspended`. Storybook story covers EN + AR + endpoint-shipped + endpoint-not-yet-shipped states.
- [ ] T032 [US1] [P] Create `apps/admin_web/components/customers/profile/role-chips.tsx`.
- [ ] T033 [US1] [P] Create `apps/admin_web/components/customers/profile/address-book-preview.tsx` (top 3 + "view all" expanding inline).
- [ ] T034 [US1] [P] Create `apps/admin_web/app/(admin)/customers/[customerId]/addresses/page.tsx` for the dedicated expanded route + the inline-expanded view sharing the same `<AddressBookExpanded>` component.
- [ ] T035 [US1] [P] Create `apps/admin_web/components/customers/profile/orders-summary-card.tsx` per research §R7 (SWR `staleTime: 60_000`); honors `flags.adminOrdersShipped` for chip target.
- [ ] T036 [US1] [P] Create `apps/admin_web/app/api/customers/[customerId]/route.ts` (GET profile) proxy.
- [ ] T037 [US1] [P] Create `apps/admin_web/app/api/customers/[customerId]/addresses/route.ts` (GET) proxy.
- [ ] T038 [US1] [P] Create `tests/unit/customers/profile/{identity-card,role-chips,address-book-preview,orders-summary-card}.test.tsx`.
- [ ] T039 [US1] [P] Add Storybook stories for the profile (default / B2B / suspended / closed / masked-PII / unmasked-PII).
- [ ] T040 [US1] [P] Create `tests/visual/customers/profile.spec.ts`.

### Story 1 e2e

- [ ] T041 [US1] Create `e2e/customers/story1_find_open_profile.spec.ts` covering filter / search / open + masked-vs-unmasked PII verification on Chromium + Firefox + WebKit.

**Checkpoint**: US1 (MVP) ships independently. Customer-support entry point lives.

---

## Phase 4: User Story 2 — Admin actions (suspend / unlock / password-reset trigger) (Priority: P2)

**Goal**: confirmation + reason note + step-up + cascade verification + audit emission.

### Action UI

- [ ] T042 [US2] Create `apps/admin_web/components/customers/actions/account-actions-section.tsx` (the section under the profile that hosts the three action buttons; consumes `evaluateAccountAction` from T006).
- [ ] T043 [US2] [P] Create `apps/admin_web/components/customers/actions/action-confirmation-shell.tsx` — the shared confirmation-dialog wrapper that captures the reason note + chains into `<StepUpDialog>` + handles 412 conflict.
- [ ] T044 [US2] [P] Create `apps/admin_web/components/customers/actions/suspend-dialog.tsx` (uses the shell with suspend-specific copy citing the cascade — sessions revoked, in-flight orders untouched, server-side cart left intact, reservations expire on TTL per FR-014). Per FR-014a, after a successful suspend the profile MUST surface a transient "session revoke pending — sessions will end within 60 s" inline status until spec 004 confirms revoke completion. The status is hidden when spec 004 publishes an atomic-suspend endpoint (the `accountState=suspended` response itself implies the cascade completed). Test under `tests/unit/customers/actions/suspend-non-atomic-status.test.tsx` covers (a) atomic endpoint → no transient status, (b) non-atomic endpoint → status renders, (c) revoke-confirmed event → status disappears.
- [ ] T045 [US2] [P] Create `apps/admin_web/components/customers/actions/unlock-dialog.tsx` (uses the shell).
- [ ] T046 [US2] [P] Create `apps/admin_web/components/customers/actions/password-reset-trigger-dialog.tsx` (uses the shell — copy notes that the customer will receive a reset link via spec 004's channel).

### Server proxies

- [ ] T047 [US2] [P] Create `apps/admin_web/app/api/customers/[customerId]/suspend/route.ts` proxy. Forwards `Idempotency-Key` + `X-StepUp-Assertion` headers + reason note body to spec 004.
- [ ] T048 [US2] [P] Create `apps/admin_web/app/api/customers/[customerId]/unlock/route.ts` proxy.
- [ ] T049 [US2] [P] Create `apps/admin_web/app/api/customers/[customerId]/password-reset/route.ts` proxy.

### Cache invalidation hook

- [ ] T050 [US2] Create `apps/admin_web/lib/customers/invalidations.ts` exporting `invalidateAfterAction({ customerId, queryClient })` doing the surgical invalidation per research §R6 (profile + orders summary + global list cache).

### SC-004 contract test + SC-005 reconciliation

- [ ] T051 [US2] Create `tests/contract/customers/no-403-after-render.spec.ts` walking every (action × customerState × permission profile × isSelf) combination and asserting the gate's decision matches the server.
- [ ] T052 [US2] [P] Create `tests/unit/customers/actions/{suspend-dialog,unlock-dialog,password-reset-trigger-dialog,action-confirmation-shell}.test.tsx`.
- [ ] T053 [US2] [P] Add Storybook stories for the three dialogs (idle / step-up-required / submitting / 412-conflict / failed) × locale × theme.
- [ ] T054 [US2] [P] Create `tests/visual/customers/actions.spec.ts`.
- [ ] T055 [US2] [P] Create `tests/a11y/customers/actions.spec.ts` (focus management on the dialog chain — confirmation → step-up → success toast).

### Story 2 e2e

- [ ] T056 [US2] Create `e2e/customers/story2_account_actions.spec.ts` covering: suspend (with step-up) → audit-entry verification → cross-app generic-auth-failure on the customer app → unlock → trigger password-reset → confirm reset link reaches the seeded inbox (per spec 004's test fixture).

**Checkpoint**: US2 ships on top of US1.

---

## Phase 5: User Story 3 — Address book + B2B company hierarchy (Priority: P3)

**Goal**: Read-only address book + B2B Company card with branches / members navigation.

- [ ] T057 [US3] Create `apps/admin_web/components/customers/profile/company-card.tsx` (hidden when `customers.b2b.read` not held).
- [ ] T058 [US3] [P] Create `apps/admin_web/components/customers/company/branches-list.tsx` rendering chips routing to each branch's profile; virtualizes via `@tanstack/react-virtual` when > 50 branches (R8).
- [ ] T059 [US3] [P] Create `apps/admin_web/components/customers/company/members-list.tsx` for `company_owner` profiles.
- [ ] T060 [US3] [P] Create `apps/admin_web/app/(admin)/customers/[customerId]/company/page.tsx` for the dedicated drill route.
- [ ] T061 [US3] [P] Create `apps/admin_web/components/customers/profile/address-book-expanded.tsx` (full list inline; reused by the dedicated `/addresses` route).
- [ ] T062 [US3] [P] Create `apps/admin_web/app/api/customers/[customerId]/company/route.ts` proxy.
- [ ] T063 [US3] [P] Create `tests/unit/customers/company/{branches-list,members-list,company-card}.test.tsx`.
- [ ] T064 [US3] [P] Add Storybook stories for `<CompanyCard>` (empty branches / dense branches / company-member kind / hidden-without-permission).
- [ ] T065 [US3] [P] Create `tests/visual/customers/company.spec.ts`.
- [ ] T066 [US3] Create `e2e/customers/story3_b2b_hierarchy.spec.ts` walking owner profile → branches list → click a branch → confirm navigation + back affordance + permission-hidden-card scenario.

**Checkpoint**: US3 ships independently on top of US1.

---

## Phase 6: User Story 4 — Cross-spec history panels (Priority: P4)

**Goal**: Verification / quote / support panels render placeholder before the owning specs ship; flip to populated render once flags flip.

- [ ] T067 [US4] [P] Create `apps/admin_web/components/customers/profile/verification-history-panel.tsx` consuming `<FeatureFlaggedPanel>` (T018). Placeholder body uses the shared `<HistoryPanelPlaceholder>` (T019). Populated render is a stub list + TODO until spec 020 ships its actual data shape.
- [ ] T068 [US4] [P] Create `apps/admin_web/components/customers/profile/quote-history-panel.tsx` (same pattern; gated on `flags.adminQuotesShipped`).
- [ ] T069 [US4] [P] Create `apps/admin_web/components/customers/profile/support-tickets-panel.tsx` (same pattern; gated on `flags.adminSupportShipped`).
- [ ] T070 [US4] [P] Mount the three panels in `apps/admin_web/app/(admin)/customers/[customerId]/page.tsx` below the orders summary card.
- [ ] T071 [US4] [P] Create `tests/unit/customers/history/{verification-history-panel,quote-history-panel,support-tickets-panel}.test.tsx` covering placeholder + (mocked) populated render.
- [ ] T072 [US4] [P] Add Storybook stories for each panel (placeholder + populated) × locale.
- [ ] T073 [US4] [P] Create `tests/visual/customers/history-panels.spec.ts`.

**Checkpoint**: US4 ships independently.

---

## Phase 7: AR/RTL editorial pass (cross-cutting)

- [ ] T074 [MANUAL] [P] Editorial-grade AR translations for every key seeded in T015/T016. **MUST NOT be executed by an autonomous agent.** Constitution Principle 4 forbids machine-translated AR. Customer-support copy carries reputational risk — a mistranslated suspend-reason or password-reset prompt can confuse already-distressed customers. Workflow: agent commits AR keys with `"@@x-source": "EN_PLACEHOLDER"` markers; human translator replaces; CI fails the AR build if any marker remains. `/speckit-implement` MUST stop at this task.
- [ ] T075 [P] Run `pnpm lint:i18n` against the customers feature; resolve any leak.
- [ ] T076 [P] Re-run all customers visual snapshots in AR-RTL — fix layout bugs (especially the masked-field copy length, the company-card branches list in RTL, and the action-dialog reason-note textarea).

---

## Phase 8: Polish & cross-cutting concerns

- [ ] T077 [P] Run `pnpm test:a11y -- --grep customers` and resolve every axe violation, with explicit attention to the masked-field announcement (screen readers must say "email hidden", not the mask glyphs) and the action-dialog focus chain.
- [ ] T078 [P] Run `pnpm test --coverage -- customers` and bring branch coverage on `lib/customers/` and `components/customers/` to ≥ 90 %.
- [ ] T079 [P] Run the **PII-leak unit sweep** `tests/unit/customers/pii-leak.test.tsx` asserting `<MaskedField>` is in the render tree of every customer-view-model consumer when the admin lacks `customers.pii.read`. SC-007 enforcement.
- [ ] T080 [P] Run the **no-403-after-render** contract test `tests/contract/customers/no-403-after-render.spec.ts`. SC-004 enforcement.
- [ ] T081 [P] Run a telemetry PII-guard sweep `tests/unit/customers/telemetry.pii-guard.test.ts` asserting no event carries customer id / email / phone / search query / reason-note text.
- [ ] T082 [P] Verify the customers feature folder adds < 200 KB gzipped to the initial JS bundle on the customers routes.
- [ ] T083 [P] Verify the audit-log reader (spec 015) renders the new customer-action audit kinds correctly: pick a few seeded events (suspend / unlock / password-reset) and confirm the JSON diff renders the actor + reason-note + step-up assertion id legibly in both locales.
- [ ] T084 [P] Lint sweep: ensure no "log in as customer" / "switch to customer" copy lands anywhere under `app/(admin)/customers/**` or `components/customers/**` (Q3 — no impersonation affordance).
- [ ] T085 [P] Ensure no direct `fetch('http…')` calls bleed into `components/customers/`.
- [ ] T085a [P] Append customer-specific gap rows to `docs/admin_web-escalation-log.md` (file authored in spec 015's T098a). One row per gap.
- [ ] T085b [P] Verify SC-009 ("0 backend contract changes shipped from this spec"). Compute `sha256` of every `services/backend_api/openapi.*.json` file at PR open time and compare against the same checksums on `main` at the branch's merge-base. CI MUST fail if any backend OpenAPI doc changed. Output the comparison table to the PR description.
- [ ] T085c [P] Per FR-001, confirm Companies and Suspended sub-entries are pre-filtered list views over `/customers` — no separate page components. Add a unit test under `tests/unit/customers/nav.test.ts` asserting `/customers?roleScope=company` and `/customers?accountState=suspended` route to the same `<CustomersList>` with preset (non-removable) filter chips, per `contracts/routes.md`.
- [ ] T085d [P] Per FR-005, verify the `closed` account-state filter renders read-only and no action button surfaces for `closed`-state customers in the SC-004 contract test (T051).
- [ ] T085e [P] Per FR-025a, append the customers permission keys to `specs/phase-1C/015-admin-foundation/contracts/permission-catalog.md` if not already present. Ensure `pnpm catalog:check-permissions` (spec 015 T032c) passes.
- [ ] T085f [P] Per spec 015 T032d, author `apps/admin_web/lib/auth/nav-manifest-static/customers.json` declaring the Customers group + sub-entries (Customers, Companies gated on `customers.b2b.read`, Suspended) per `contracts/nav-manifest.md` order range 500–599. Ensure `pnpm catalog:check-nav-manifest` (spec 015 T032e) passes.
- [ ] T085g [P] Per FR-022a, ensure the suspend reason-note path (`accountAction.reasonNote`) is registered in `contracts/audit-redaction.md` (done) and that the audit-log reader's JSON viewer correctly redacts it for `audit.read`-only admins. Add a fixture-driven test.
- [ ] T086 Author DoD checklist evidence for SC-001 → SC-009 in the PR description.
- [ ] T087 Open the PR with: spec link, plan link, story-by-story demos (screen recordings or Storybook links), CI green, fingerprint marker.

---

## Dependencies

| Phase | Depends on |
|---|---|
| Phase 1 (Setup) | spec 015 merged + spec 004 contract merged |
| Phase 2 (Foundational) | Phase 1; T010 (step-up promotion) ideally lands first across the admin app |
| Phase 3 (US1) | Phase 2 |
| Phase 4 (US2) | Phase 2 + Phase 3 (action buttons live on the profile) |
| Phase 5 (US3) | Phase 2 + Phase 3 (company card lives on the profile; address book expanded reuses the preview) |
| Phase 6 (US4) | Phase 2 + Phase 3 (panels mount on the profile) |
| Phase 7 (AR/RTL) | Phase 3 + Phase 4 + Phase 5 + Phase 6 |
| Phase 8 (Polish) | All prior phases |

## Parallel-execution opportunities

- **Phase 2**: T005–T019 are largely file-disjoint; large parallel fan-out for a 3–4 engineer team.
- **Within US1**: list / filter / search / profile cards / API proxies are independent file scopes.
- **US3 / US4 can run in parallel** with US2 once Phase 3 ships — different feature folders.

## Suggested MVP scope

**MVP = Phase 1 + Phase 2 + Phase 3 (US1)** — 41 tasks — ships customers list + profile detail. Customer-support teams can identify and contextualize a customer in seconds; account actions (US2) and the rest follow as independent PRs.

## Format check

All 87 tasks follow `- [ ] Tnnn [P?] [USn?] description (path)` and include explicit file paths.
