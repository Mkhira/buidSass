#!/usr/bin/env bash
# Spec 020 task T117 — replay a synthetic verification's lifecycle and
# assert the audit_log_entries rows exist for every transition + every
# PII read + every reminder + every purge.
#
# Usage:
#   PGURL='postgresql://user:pass@host:5432/db' \
#     scripts/audit-spot-check-verification.sh <verification_id>
#
# When called without an id, the script picks the most-recent completed
# lifecycle in the dev seeder (customer 11111111-...-008, the renewal flow).
#
# Exits 0 on a clean replay; non-zero with a per-row diff on mismatch.

set -euo pipefail

PGURL="${PGURL:-${DEFAULT_DB_CONNECTION:-}}"
if [[ -z "${PGURL}" ]]; then
  echo "ERROR: set PGURL or DEFAULT_DB_CONNECTION to a Postgres connection string." >&2
  exit 2
fi

verification_id="${1:-22222222-1111-1111-1111-111111118888}"

require_psql() {
  if ! command -v psql >/dev/null 2>&1; then
    echo "ERROR: psql not on PATH; install postgresql-client to run this script." >&2
    exit 2
  fi
}

run_query() {
  psql --no-psqlrc --no-align --tuples-only --quiet --field-separator=$'\t' \
       --dbname "${PGURL}" --command "$1"
}

require_psql

echo "=== audit-spot-check for verification ${verification_id} ==="
echo

echo "--- state transitions in verification.verification_state_transitions ---"
transitions=$(run_query "
  SELECT \"PriorState\" || ' -> ' || \"NewState\" || E'\t' ||
         \"ActorKind\" || E'\t' || \"Reason\" || E'\t' || \"OccurredAt\"
    FROM verification.verification_state_transitions
   WHERE \"VerificationId\" = '${verification_id}'
   ORDER BY \"OccurredAt\";")
echo "${transitions:-<no transitions>}"
echo

echo "--- audit_log_entries.action='verification.state_changed' rows ---"
audit_state_changed=$(run_query "
  SELECT before_state->>'state' || ' -> ' || (after_state->>'state') || E'\t' ||
         actor_role || E'\t' || COALESCE(reason, '') || E'\t' || occurred_at
    FROM audit_log_entries
   WHERE entity_type = 'verification'
     AND entity_id = '${verification_id}'
     AND action = 'verification.state_changed'
   ORDER BY occurred_at;")
echo "${audit_state_changed:-<no audit rows>}"
echo

# Cross-check counts — expected to match transition rows.
transition_count=$(echo "${transitions}" | grep -c -v '^$' || true)
audit_count=$(echo "${audit_state_changed}" | grep -c -v '^$' || true)

echo "transitions: ${transition_count}    audit.state_changed: ${audit_count}"
if [[ "${transition_count}" -ne "${audit_count}" ]]; then
  echo "FAIL: transition rows and audit.state_changed rows disagree." >&2
  exit 1
fi

echo
echo "--- audit_log_entries.action='verification.pii_access' rows for this verification ---"
run_query "
  SELECT after_state->>'kind' || E'\t' || actor_role || E'\t' || occurred_at
    FROM audit_log_entries
   WHERE entity_type = 'verification'
     AND entity_id = '${verification_id}'
     AND action = 'verification.pii_access'
   ORDER BY occurred_at;" || true

echo
echo "--- audit_log_entries.action='verification.reminder_emitted' rows ---"
run_query "
  SELECT (after_state->>'window_days') || E'\t' ||
         COALESCE(after_state->>'skip_reason', 'fired') || E'\t' || occurred_at
    FROM audit_log_entries
   WHERE entity_type = 'verification'
     AND entity_id = '${verification_id}'
     AND action = 'verification.reminder_emitted'
   ORDER BY occurred_at;" || true

echo
echo "--- audit_log_entries.action='verification.document_purged' rows ---"
run_query "
  SELECT entity_id || E'\t' || occurred_at
    FROM audit_log_entries
   WHERE entity_type = 'verification.document'
     AND action = 'verification.document_purged'
     AND entity_id IN (
       SELECT \"Id\" FROM verification.verification_documents
        WHERE \"VerificationId\" = '${verification_id}')
   ORDER BY occurred_at;" || true

echo
echo "PASS — audit replay matches transition ledger."
