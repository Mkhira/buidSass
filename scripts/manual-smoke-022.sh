#!/usr/bin/env bash
# Spec 022 — manual smoke (T146).
# Runs one slice from each top-level surface per quickstart §13.
# Usage:
#   BASE_URL=https://api.staging.dental-commerce \
#   CUSTOMER_TOKEN=<jwt> \
#   ADMIN_TOKEN=<jwt> \
#   PRODUCT_ID=<guid> \
#   ORDER_LINE_ID=<guid> \
#   ./scripts/manual-smoke-022.sh
#
# Expected exit code: 0. Each step prints HTTP status + a 1-line summary.
# This is intentionally a runnable artifact that an operator pastes
# real values into — NOT a CI test. The CI surface is the contract suites
# under Tests/Reviews.Tests/Contract/.

set -euo pipefail

: "${BASE_URL:?BASE_URL must be set, e.g. https://api.staging.dental-commerce}"
: "${CUSTOMER_TOKEN:?CUSTOMER_TOKEN must be set (a customer-surface JWT)}"
: "${ADMIN_TOKEN:?ADMIN_TOKEN must be set (an admin-surface JWT with reviews.moderator)}"
: "${PRODUCT_ID:?PRODUCT_ID must be set}"
: "${ORDER_LINE_ID:?ORDER_LINE_ID must be set (a delivered, non-refunded order-line for the customer + product)}"

CUST_HEADERS=(-H "Authorization: Bearer ${CUSTOMER_TOKEN}" -H "Content-Type: application/json")
ADMIN_HEADERS=(-H "Authorization: Bearer ${ADMIN_TOKEN}" -H "Content-Type: application/json")

echo "== 1. Customer submit =="
SUBMIT=$(curl -sS -w '\n%{http_code}' -X POST "${BASE_URL}/api/customer/reviews" "${CUST_HEADERS[@]}" \
  -d "{\"productId\":\"${PRODUCT_ID}\",\"rating\":5,\"headline\":\"Smoke test\",\"body\":\"Manual smoke per quickstart §13.\",\"locale\":\"en\"}")
SUBMIT_CODE=$(echo "$SUBMIT" | tail -n1)
SUBMIT_BODY=$(echo "$SUBMIT" | sed '$d')
echo "  HTTP ${SUBMIT_CODE} — $(echo "$SUBMIT_BODY" | jq -c '{id, state, pendingReview}' 2>/dev/null || echo "$SUBMIT_BODY")"
[[ "$SUBMIT_CODE" == "201" ]] || { echo "  EXPECTED 201"; exit 1; }
REVIEW_ID=$(echo "$SUBMIT_BODY" | jq -r .id)

echo "== 2. Customer edit =="
EDIT=$(curl -sS -w '\n%{http_code}' -X PATCH "${BASE_URL}/api/customer/reviews/${REVIEW_ID}" "${CUST_HEADERS[@]}" \
  -d '{"rating":4}')
EDIT_CODE=$(echo "$EDIT" | tail -n1)
echo "  HTTP ${EDIT_CODE}"
[[ "$EDIT_CODE" == "200" ]] || { echo "  EXPECTED 200"; exit 1; }

echo "== 3. Customer list-mine =="
LIST=$(curl -sS -w '\n%{http_code}' "${BASE_URL}/api/customer/reviews/me" "${CUST_HEADERS[@]}")
LIST_CODE=$(echo "$LIST" | tail -n1)
LIST_BODY=$(echo "$LIST" | sed '$d')
echo "  HTTP ${LIST_CODE}"
[[ "$LIST_CODE" == "200" ]] || { echo "  EXPECTED 200"; exit 1; }
# Semantic — the just-submitted review must appear in the caller's list.
LIST_HAS=$(echo "$LIST_BODY" | jq --arg id "$REVIEW_ID" '[.items[]? | select(.id == $id)] | length' 2>/dev/null || echo 0)
[[ "$LIST_HAS" == "1" ]] || { echo "  EXPECTED submitted review ${REVIEW_ID} to appear in /me list (got $LIST_HAS)"; exit 1; }

echo "== 4. Customer report (skipped — needs a second customer + a different review id) =="

echo "== 5. Admin queue =="
QUEUE=$(curl -sS -w '\n%{http_code}' "${BASE_URL}/api/admin/reviews/queue?state=pending_moderation" "${ADMIN_HEADERS[@]}")
QUEUE_CODE=$(echo "$QUEUE" | tail -n1)
QUEUE_BODY=$(echo "$QUEUE" | sed '$d')
echo "  HTTP ${QUEUE_CODE}"
[[ "$QUEUE_CODE" == "200" ]] || { echo "  EXPECTED 200"; exit 1; }
# Semantic — queue response must expose the documented shape (items[] + nextCursor).
echo "$QUEUE_BODY" | jq -e '(.items | type == "array") and (has("nextCursor"))' >/dev/null \
  || { echo "  EXPECTED { items: array, nextCursor: string|null }"; exit 1; }
# Semantic — every queued row must surface the moderator-relevant fields.
QUEUE_FIELDS_OK=$(echo "$QUEUE_BODY" | jq '[.items[]? | (has("id") and has("state") and has("rating") and has("createdAtUtc"))] | all' 2>/dev/null || echo false)
[[ "$QUEUE_FIELDS_OK" == "true" ]] || { echo "  EXPECTED queue items to expose id/state/rating/createdAtUtc"; exit 1; }

echo "== 6. Admin decide (skipped — would mutate live state; run manually if a flagged review exists) =="

echo "== 7. Public aggregate read =="
AGG=$(curl -sS -i "${BASE_URL}/api/public/reviews/aggregates/${PRODUCT_ID}?market_code=SA")
AGG_CODE=$(printf '%s' "$AGG" | awk 'NR==1{print $2; exit}')
AGG_BODY=$(printf '%s' "$AGG" | awk 'BEGIN{b=0} /^\r?$/{b=1; next} b{print}')
AGG_CACHE=$(printf '%s' "$AGG" | awk 'tolower($1)=="cache-control:" {sub(/^[^:]*:[ \t]*/, ""); print; exit}')
echo "  HTTP ${AGG_CODE} — $(echo "$AGG_BODY" | jq -c '{reviewCount, avgRating}' 2>/dev/null || echo "$AGG_BODY") cache=${AGG_CACHE}"
[[ "$AGG_CODE" == "200" ]] || { echo "  EXPECTED 200"; exit 1; }
# Semantic — aggregate response must expose the documented shape.
echo "$AGG_BODY" | jq -e '(.reviewCount | type == "number") and (has("avgRating"))' >/dev/null \
  || { echo "  EXPECTED { reviewCount: number, avgRating: number|null }"; exit 1; }
# Semantic — Cache-Control: public, max-age=60 per contract §6.1.
[[ "$AGG_CACHE" =~ public ]] && [[ "$AGG_CACHE" =~ max-age=60 ]] \
  || { echo "  EXPECTED Cache-Control: public, max-age=60 (got '${AGG_CACHE}')"; exit 1; }

echo
echo "Smoke complete. Paste this output into the merge PR description."
