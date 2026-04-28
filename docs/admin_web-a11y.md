# `apps/admin_web/` — Accessibility checklist (WCAG 2.1 AA)

Per spec 015 FR-005: the admin shell and every page rendered inside
it MUST meet WCAG 2.1 AA end-to-end. This file is the per-page
checklist; CI (`pnpm test:a11y`, T091) enforces axe-clean across
every shell + audit page.

## Per-page checklist (re-run on every shell-touching PR)

For each page, walk:

- [ ] **Keyboard navigation** — every interactive element reachable via Tab in a logical order. No keyboard traps.
- [ ] **Focus rings** — visible focus indicator on every focusable element (uses `focus-visible:ring-3 ring-ring/50` from the design tokens).
- [ ] **Skip-to-content link** — the AppShell renders one (`<a href="#main-content">`) per FR-005. Verify it appears on focus, navigates to the main content area, and the main has `tabIndex={-1}` so the focus lands.
- [ ] **Headings** — single `<h1>` per page; `<h2>` / `<h3>` form a logical outline (no skipped levels).
- [ ] **Landmarks** — `<header>` / `<nav>` / `<main>` / `<footer>` (and ARIA equivalents) present. Each `<nav>` has an `aria-label`.
- [ ] **Form labels** — every input has a programmatically associated `<Label htmlFor>` or `aria-label`.
- [ ] **Error messages** — surfaced via `role="alert"` or `aria-live="assertive"`. Form errors keyed to the field via `aria-describedby`.
- [ ] **Color contrast** — text ≥ 4.5:1 against background, large text + UI ≥ 3:1. axe enforces. Brand-overlay semantic colors (`brand.success/warning/error/info`) tested in both light + dark themes.
- [ ] **RTL** — every page renders correctly with `dir="rtl"`. Logical-property utilities (`me-`, `ms-`, `pe-`, `ps-`) auto-flip; `tools/lint/no-physical-margins.ts` enforces.
- [ ] **Live regions** — async content (loading skeletons, toast notifications, validation errors) announces via `aria-live` or `role="status"` / `role="alert"`.
- [ ] **Modal / dialog focus** — focus trapped inside on open; restored to the trigger on close. Esc closes. (shadcn primitives handle this; verify when wrapping.)
- [ ] **Tables** — `<th scope="col">` on headers; `aria-rowcount` / `aria-rowindex` on virtualized rows.

## Page-by-page

### `/` (landing)

- Two placeholder cards. Heading hierarchy: `h1` (app name) → `h2` (card titles).
- No interactive elements outside the shell + topbar.

### `/audit` (list)

- Filter panel: every input has a `<Label>`. Apply / Clear buttons keyboard-reachable.
- Table: row links are `<a>`, not `<div onClick>`.
- Pagination: prev/next buttons disabled state visible + `aria-disabled` set.

### `/audit/[entryId]` (detail)

- JSON diff viewer: each pane is a `<section>` with an `aria-label`.
- Permalink-copy button: `aria-busy` while writing to clipboard; toast announces success via `role="alert"`.
- `<MaskedField>`: redacted values announce the localized "X hidden" via `aria-label`, not the mask glyph.

### `/login` / `/mfa` / `/reset[/confirm]`

- Form errors via `role="alert"` at the form top + `aria-describedby` on individual fields.
- TOTP input: `inputMode="numeric"`, `autoComplete="one-time-code"`, `pattern="\d{6}"`.
- Password fields: `autoComplete="current-password"` / `"new-password"` for password managers.

### `/me` / `/me/preferences`

- Read-only profile renders as `<dl>` with paired `<dt>` / `<dd>`.
- Saved views list is keyboard-navigable; empty state announces via `role="status"`.

### `/__forbidden` / `/__not-found`

- `<main role="main">` with single `<h1>`. CTA button reachable via Tab.

## Shared primitives a11y checklist

(One row per shell primitive; each has a Storybook a11y story when shipped.)

| Primitive | a11y notes |
|---|---|
| `<AppShell>` | Skip-to-content + tabbable `<main>` |
| `<SidebarNav>` | `<nav aria-label>`; active route uses `aria-current="page"` |
| `<TopBar>` | Bell button has `aria-label`; locale toggle is `role="group"` with toggle buttons + `aria-pressed` |
| `<DataTable>` | `aria-rowcount` / `aria-rowindex` on virtualized rows when virtualization lands |
| `<FormBuilder>` | `aria-required`, `aria-invalid`, `aria-describedby` wired automatically |
| `<MaskedField>` | `aria-label` carries the "Email hidden" / "Phone hidden" label; mask glyph is `aria-hidden` |
| `<ConfirmationDialog>` | shadcn Dialog primitive handles focus trap + ESC + restore |
| `<StepUpDialog>` | TOTP input `autoComplete="one-time-code"`; submit `aria-busy` while verifying |
| `<ConflictReloadDialog>` | Preserved-fields side panel is `role="region"` with `aria-label` |
| `<AuditForResourceLink>` | Standard link-styled-as-button; respects `audit.read` (hidden when missing) |
| `<ExportJobStatus>` | Status pill rendered as `<Badge>`; in-progress percentage announces via `aria-live="polite"` |

## Verification cadence

- **Per PR**: `pnpm test:a11y` runs axe-playwright against every shell + audit page (T091). Zero new violations to merge.
- **Nightly** (T032h): full Storybook visual + a11y suite, all 12 primitives × locale × theme.
- **Pre-launch**: manual keyboard walkthrough against this checklist by an admin who didn't write the code.
