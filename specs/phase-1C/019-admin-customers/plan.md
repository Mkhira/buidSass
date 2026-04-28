# Implementation Plan: Admin Customers

**Branch**: `phase-1C-specs` | **Date**: 2026-04-27 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/phase-1C/019-admin-customers/spec.md`

## Summary

Mount the **customer-support module** inside spec 015's admin shell — Customers list, Profile detail (identity + roles + addresses + orders summary), Account actions (suspend / unlock / password-reset trigger), Address book (read-only), B2B Company hierarchy, plus three feature-flagged history panels (verification / quotes / support tickets) that flip on when their owning specs (020 / 021 / 023) ship. Lane B: UI only — every backend gap escalates to spec 004.

PII columns are gated by `customers.pii.read` with a defence-in-depth UI mask backed by spec 004's server-side redaction (FR-007 + Q4 — no audit emission on UI redacted views; spec 004's data-access layer is the audit-bearing surface). Admin actions reuse spec 018's step-up dialog primitive (now promoted into spec 015's shell as a shared component) plus a mandatory ≥ 10-char reason note. Suspend cascades into immediate session revocation via spec 004 but leaves orders + reservations untouched (Q1). The customer app surfaces a generic auth-failure to a suspended customer (Q2 — no suspension-state leak). Customer impersonation is **not** in scope and ships **no placeholder** anywhere (Q3). Order-summary chip uses stale-while-revalidate with a 60 s window (Q5).

The shell, auth proxy, `DataTable`, `FormBuilder`, audit-log read surface, AR-RTL plumbing, telemetry adapter, and CI hygiene are all inherited from specs 015 / 016 / 017 / 018 unchanged. The only new infra: a `<MaskedField>` component used everywhere a PII value renders (so the redaction logic is single-sourced).

## Technical Context

**Language/Version**: TypeScript 5.5, Node.js 20 LTS (inherits spec 015's runtime).

**Primary Dependencies** (deltas on top of specs 015 / 016 / 017 / 018):

- No new runtime deps. The B2B company-hierarchy view uses the same virtualization (`@tanstack/react-virtual` ^3) and DnD primitives the catalog tree (016) already uses, when needed for large branch lists; small B2B groups render flat without virtualization.
- Reuses spec 018's `<StepUpDialog>` (now relocated to `apps/admin_web/components/shell/step-up-dialog.tsx` as a shared shell primitive — see Open follow-ups in research §R5 if not already promoted).
- Reuses spec 015's `DataTable`, `FormBuilder`, state primitives.

**Storage**: No new server-side persistence. Client-side: react-query cache (60 s stale window for order-summary per Q5; default windows for everything else); transient `idb` only to persist in-flight reason notes if the admin survives a tab crash mid-typing.

**Testing**:

- Unit + component (vitest + RTL) — list filters, masked-field redaction, profile cards, account-action dialogs, B2B company hierarchy, history panels (placeholder + populated).
- Visual regression (Playwright + Storybook snapshots) — every customer screen × {EN-LTR, AR-RTL} × {light, dark}; explicit stories for masked vs. unmasked PII.
- A11y (axe-playwright) — every customer screen, with explicit checks on the masked-field component (the mask must remain readable as a placeholder for screen readers, not announce as the actual value).
- E2E (Playwright) — Story 1 (find + open profile), Story 2 (suspend + step-up + audit verification + cross-app generic-auth-failure verification), Story 3 (B2B hierarchy navigation).
- A "no-403-after-render" contract test for admin actions, mirroring spec 018's pattern: every action × permission profile, action button is **either** rendered with a valid path OR hidden — never rendered then 403-on-click.
- A PII-leak unit test sweeping every component that consumes the customer view-model — asserts the masked-field renders for an admin without `customers.pii.read`.

**Target Platform**: Same as spec 015 — modern desktop browsers ≥ 1280 px wide.

**Project Type**: Next.js admin web feature folder under `apps/admin_web/app/(admin)/customers/` and `apps/admin_web/components/customers/`. No new app or package.

**Performance Goals**:

- Customers list first page ≤ 1 s on staging dataset (1M lifetime, 50k active customers) — SC-002.
- Profile detail first interactive ≤ 1.5 s on broadband — SC-006.
- Free-text search median latency ≤ 500 ms (server is authoritative).

**Constraints**:

- **No backend code in this PR** (FR-026). Gaps escalate to spec 004 / 020 / 021 / 023.
- **No client-side fetch outside `lib/api/`** (inherits spec 015's lint).
- **No hard-coded user-facing strings** outside `messages/{en,ar}.json` (inherits 015's i18n lint).
- **No PII in client logs / telemetry** — strict guard rails per `contracts/client-events.md`.
- **No customer-impersonation affordance** — explicit lint rule blocks any "log in as customer" / "switch to customer" copy from landing in `app/(admin)/customers/**`.
- **Admin actions hidden when not allowed** (FR-012) — same gate model as spec 018.

**Scale/Scope**: ~6 customers pages (list, profile, profile/account-actions confirmation, profile/addresses-expanded, B2B branches list, B2B company drill). 4 prioritized user stories, 28 functional requirements, 9 success criteria, 5 clarifications integrated. Storybook target: ~20 stories on top of 015's baseline (heavily reuses step-up dialog + masked-field stories).

## Constitution Check

| Principle / ADR | Gate | Status |
|---|---|---|
| P3 Experience Model | Customer-app browse / view price unaffected — admin side. | PASS (n/a) |
| P4 Arabic / RTL editorial | Every customers screen ships AR + EN with RTL via spec 015's i18n stack. Verification-state, account-state, role labels are localized via i18n keys (FR-028). | PASS |
| P5 Market Configuration | List exposes a market filter; admin's role scope clamps results. No hard-coded market literals. | PASS |
| P6 Multi-vendor-ready | Forward-compatible. When spec 004 / spec 021 add vendor-scope on B2B accounts (Phase 2), the company-hierarchy view renders whatever the server sends. | PASS |
| P7 Branding | Tokens consumed from `packages/design_system`. No inline hex literals. | PASS |
| P9 B2B | The B2B company-hierarchy card surfaces parent + branches + member-admins (FR-020). B2B-specific workflows (approver re-routing, etc.) deferred to spec 021. | PASS (forward-compatible) |
| P22 Fixed Tech | Next.js + shadcn/ui per ADR-006. | PASS |
| P23 Architecture | Spec 015's modular shell + this feature folder. No new service. | PASS |
| P24 State Machines | Account-action submission state (Idle / StepUpRequired / Submitting / ConflictDetected / Failed) — documented in `data-model.md`. | PASS |
| P25 Data & Audit | Every admin action emits an audit event server-side via spec 004; the audit-log reader (spec 015) is the read surface. PII redacted views are NOT audit-emitting per Q4 — server-side data-access layer is the audit-bearing surface. | PASS |
| P27 UX Quality | Every screen ships loading / empty / error / restricted / conflict (412) / step-up-required / locale-switch states. | PASS |
| P28 AI-Build Standard | Spec ships explicit FRs, scenarios, edge cases, success criteria, 5 resolved clarifications. | PASS |
| P29 Required Spec Output | All 12 sections present. | PASS |
| P30 Phasing | Phase 1C Milestone 5/6. Depends on spec 004 contract merged + spec 015 shipped. | PASS |
| P31 Constitution Supremacy | No conflicts. | PASS |
| ADR-001 Monorepo | Code under `apps/admin_web/`. | PASS |
| ADR-006 Next.js + shadcn/ui | Locked. | PASS |
| ADR-010 KSA residency | API calls hit Azure Saudi Arabia Central. | PASS |

**No violations.**

## Project Structure

### Documentation (this feature)

```text
specs/phase-1C/019-admin-customers/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── consumed-apis.md
│   ├── routes.md
│   └── client-events.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
apps/admin_web/
├── app/(admin)/customers/
│   ├── layout.tsx                       # Sub-shell highlighting the customers sidebar group
│   ├── page.tsx                         # Customers list (DataTable)
│   └── [customerId]/
│       ├── page.tsx                     # Profile detail
│       ├── addresses/page.tsx           # Address book expanded
│       └── company/page.tsx             # B2B company drill (only when relevant + permission held)
├── components/customers/
│   ├── list/
│   │   ├── customers-table.tsx          # Wraps spec 015's DataTable
│   │   ├── filter-bar.tsx               # Market / B2B / verification / account-state filters
│   │   └── search-bar.tsx               # Server-side free-text
│   ├── profile/
│   │   ├── identity-card.tsx
│   │   ├── role-chips.tsx
│   │   ├── address-book-preview.tsx
│   │   ├── orders-summary-card.tsx      # 60s stale-while-revalidate
│   │   ├── company-card.tsx             # Hidden without customers.b2b.read
│   │   ├── verification-history-panel.tsx
│   │   ├── quote-history-panel.tsx
│   │   └── support-tickets-panel.tsx
│   ├── actions/
│   │   ├── account-actions-section.tsx  # Hosts suspend / unlock / password-reset
│   │   ├── suspend-dialog.tsx
│   │   ├── unlock-dialog.tsx
│   │   ├── password-reset-trigger-dialog.tsx
│   │   └── action-confirmation-shell.tsx # Reason note + step-up wrapper
│   ├── company/
│   │   ├── branches-list.tsx
│   │   └── members-list.tsx
│   └── shared/
│       ├── masked-field.tsx             # Single-source PII redaction
│       ├── conflict-overlay.tsx
│       └── feature-flagged-panel.tsx    # Wrapper for the three history panels
├── lib/customers/
│   ├── action-state.ts                  # SM-1 client model
│   ├── pii-mask.ts                      # Pure formatter for masked email / phone
│   ├── feature-flags.ts                 # adminVerificationsShipped, adminQuotesShipped, adminSupportShipped, adminOrdersShipped
│   └── api.ts                           # react-query hooks wrapping spec 004 client
└── tests/
    ├── unit/customers/...
    ├── visual/customers.spec.ts
    └── contract/customers.no-403-after-render.spec.ts   # Mirrors spec 018's pattern
```

**Structure Decision**: One feature folder under `app/(admin)/customers/` mirroring the route structure. Components live under `components/customers/<noun>/` mirroring the route. `lib/customers/` holds the action-state + PII-mask + feature-flag map. The masked-field component is the only meaningful new shared primitive — every customer view-model consumer routes PII through it. Step-up dialog reuses the shell primitive from spec 015 (relocated from spec 018 if not already shared).

## Complexity Tracking

| Choice | Why | Simpler alternative rejected because |
|---|---|---|
| Single-source `<MaskedField>` for every PII display | One component → one mask format → one a11y story → one snapshot story per locale × theme. The PII-leak unit test sweeps every consumer of the customer view-model and asserts MaskedField is in the render path. | Inline ternaries in each component drift the moment a permission key is renamed or a new PII column is added. |
| Hide-not-disable for admin actions (FR-012, mirrors spec 018 SC-004) | Disabled buttons train admins that "this should work" — they get clicked, return 403, look broken. Hide reflects the actual capability set. | Disabled-but-rendered actions clutter the screen and produce 403 errors that look like bugs. |
| Stale-while-revalidate orders summary (Q5) | Order count + most-recent-order id is low-velocity relative to support sessions. SWR with 60 s stale balances UX freshness against backend load. | Fresh-on-every-mount adds N×K backend calls for K admins doing K profile open-and-close cycles per shift. |
| No customer-impersonation affordance, no placeholder (Q3) | Compliance + audit cost of impersonation warrants a dedicated spec. Placing any affordance now makes future removal painful and discoverability-leaky. | A "coming soon" placeholder is harder to remove than to add when (if) the dedicated spec ships. |
| Three history panels behind feature flags (FR-022 / 023 / 024) | Decouples spec 019's ship date from specs 020 / 021 / 023. Operations flip flags as those specs land without an 019 redeploy. | Hard-coded "coming soon" requires a code change per flip. |
| Shared step-up dialog promoted to shell from spec 018 | Both 018 (refunds) and 019 (account actions) need step-up; the dialog belongs in the shell, not in either feature folder. | Per-feature copies drift; one canonical step-up flow keeps the spec 004 contract surface stable. |
