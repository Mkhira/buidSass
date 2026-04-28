# Finance CSV export format

This spec **does not own** the CSV header schema — it's published by spec 011.

## Source of truth

`specs/phase-1B/011-orders/contracts/finance-export.md` (or whichever path spec 011 uses).

## UI contract

The export UI promises:

- The downloaded CSV's first row is the header row, exactly matching the schema spec 011 published at the time of the job's creation.
- The order detail page surfaces an "export this filtered set" affordance that creates a new job with the **current** filter snapshot.
- The job-detail page renders the snapshot's filters read-only so admins can verify what was exported (FR-021).
- The CSV carries:
  - Locale-correct numerals (Western Arabic) — the file is consumed by finance tooling that expects deterministic numerals.
  - An explicit currency column per row (multi-market exports happen — finance teams reconcile across both KSA + EG).
  - Date columns in ISO-8601 UTC.
  - One row per order; for a refunded order the row reflects the **net** amount (captured − refunded). A separate refund-events export is out of scope for v1.

## Schema bump banner

If spec 011 bumps the export schema between create-time and download-time, the job's `filterSnapshot` carries the snapshot's schema version. The job-detail page surfaces a banner if the live schema is newer, directing the admin to re-export to get the latest schema.

## Escalation

If spec 011's export schema document is missing or under-specified, file `spec-011:gap:finance-export-schema-publication`. Until resolved, the export action is hidden behind a feature flag (`flags.financeExportEnabled`).
