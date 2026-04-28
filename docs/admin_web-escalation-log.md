# `apps/admin_web/` — Escalation log

Per spec 015 FR-029 and the same FR in 016 / 017 / 018 / 019: every
backend gap discovered while implementing the admin web app is **filed
as a separate issue against the owning Phase 1B spec**, not patched in
the admin PR. This log is the manifest of those issues — one row per
gap.

An empty log on merge is acceptable. An absent log file fails the PR
(a CI-step grep checks for the file's existence).

## Format

| Date | Owning spec | Gap title | GitHub issue | In-app workaround |
|---|---|---|---|---|

## Open gaps

| Date | Owning spec | Gap title | GitHub issue | In-app workaround |
|---|---|---|---|---|
| 2026-04-27 | spec-004 (identity) | `permission-catalog-endpoint` — `/v1/admin/permission-catalog` not yet shipped | TBD (file when 004 starts implementation) | Drift CI (`pnpm catalog:check-permissions`) is a no-op until the endpoint ships; the markdown registry is the source of truth client-side. |
| 2026-04-27 | spec-004 (identity) | `nav-manifest-loader` — server-driven manifest endpoint not yet shipped | TBD | `USE_STATIC_NAV_MANIFEST=1` (default); shell composes from `lib/auth/nav-manifest-static/<module>.json` build-time files. Cutover is one env-flip per FR-028g. |
| 2026-04-27 | spec-004 (identity) | `user-preferences-endpoint` — generic preferences blob not yet shipped | TBD | `<SavedViewsBar>` falls back to localStorage keyed `admin_pref:dataTable:<viewKey>`. Promotion is one storage-adapter swap per FR-023's escalation. |
| 2026-04-27 | spec-023 (notifications) | `sse-stream-endpoint` — admin notification SSE upstream not yet shipped | TBD | Bell consumes the stub feed (`NEXT_PUBLIC_NOTIFICATIONS_STUB=1`); SSE proxy emits heartbeat-only stream until upstream lands. |
| 2026-04-27 | spec-003 (foundations) | `audit-log-pii-redaction` — server-side pre-redaction of sensitive paths not yet confirmed | TBD | `<JsonDiffViewer>` enforces field-level redaction client-side via `redaction-policy.ts`. CI drift check (`pnpm catalog:check-audit-redaction`) walks fixtures and asserts every sensitive path is registered. |

## Closed gaps

(Empty — to be appended as upstream specs ship and remove the workaround.)

## Module-specific append rule

When 016 / 017 / 018 / 019 surface new gaps during their implementation,
they append rows to **this same file** under the **Open gaps** table.
Spec 016 appends `spec-005:gap:*`, 017 appends `spec-008:gap:*`, etc.

The table's "Owning spec" column is the gating filter — `spec-004:gap:*`
issues block any module that depends on spec 004 if marked `blocking`,
otherwise they're worked around per the row's "In-app workaround"
column. The workaround column is the contract that lets an admin module
ship while a 1B gap stays open.
