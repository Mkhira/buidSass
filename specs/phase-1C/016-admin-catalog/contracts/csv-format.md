# Bulk-import CSV format

This spec **does not own** the CSV header schema. Spec 005 (catalog) publishes the canonical schema; this document is a pointer + a contract for what the UI promises.

## Source of truth

- `specs/phase-1B/005-catalog/contracts/bulk-import.md` (or whichever path spec 005 uses) is the canonical schema.
- The Next.js Route Handler that serves the export streams the response from spec 005's export endpoint untouched — the UI never re-orders columns.

## UI contract

The bulk-import UI promises:

- The downloaded export's first row is the header row, exactly matching the schema spec 005 published at the time of download.
- An admin who exports → modifies → re-uploads the same file MUST get a clean validation report (header match) regardless of how many minutes have passed, *unless* spec 005's schema version has bumped in the meantime — in which case the dry-run surfaces a single header-mismatch error pointing at the new schema URL.
- Validation report rows include: `row_number` (1-indexed, matching the original CSV including header), `column_name`, `reason_code`, `human_message_en`, `human_message_ar`. The UI renders the locale-matching `human_message_*`.
- The schema bump cadence is owned by spec 005's release notes; this UI surfaces a banner when an upload's schema version disagrees with the current export's schema version.

## Escalation

If spec 005's CSV header schema document is missing or under-specified, file `spec-005:gap:csv-schema-publication`. Until resolved, the bulk-import wizard renders the upload step with a "schema not yet published" notice — admins fall back to one-by-one editing.
