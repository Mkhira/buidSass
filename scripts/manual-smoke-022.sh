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
echo "  HTTP ${LIST_CODE}"
[[ "$LIST_CODE" == "200" ]] || { echo "  EXPECTED 200"; exit 1; }

echo "== 4. Customer report (skipped — needs a second customer + a different review id) =="

echo "== 5. Admin queue =="
QUEUE=$(curl -sS -w '\n%{http_code}' "${BASE_URL}/api/admin/reviews/queue?state=pending_moderation" "${ADMIN_HEADERS[@]}")
QUEUE_CODE=$(echo "$QUEUE" | tail -n1)
echo "  HTTP ${QUEUE_CODE}"
[[ "$QUEUE_CODE" == "200" ]] || { echo "  EXPECTED 200"; exit 1; }

echo "== 6. Admin decide (skipped — would mutate live state; run manually if a flagged review exists) =="

echo "== 7. Public aggregate read =="
AGG=$(curl -sS -w '\n%{http_code}' "${BASE_URL}/api/public/reviews/aggregates/${PRODUCT_ID}?market_code=SA")
AGG_CODE=$(echo "$AGG" | tail -n1)
AGG_BODY=$(echo "$AGG" | sed '$d')
echo "  HTTP ${AGG_CODE} — $(echo "$AGG_BODY" | jq -c '{reviewCount, avgRating}' 2>/dev/null || echo "$AGG_BODY")"
[[ "$AGG_CODE" == "200" ]] || { echo "  EXPECTED 200"; exit 1; }

echo
echo "Smoke complete. Paste this output into the merge PR description."
