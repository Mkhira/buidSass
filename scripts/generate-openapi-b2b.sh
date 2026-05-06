#!/usr/bin/env bash
# Spec 021 T148 — generate openapi.b2b.json from a running app.
#
# Mirrors scripts/generate-openapi-reviews.sh: boots backend_api on a free
# port, fetches /openapi/v1.json, filters to the spec-021 path namespaces,
# and writes the result to services/backend_api/openapi.b2b.json.
#
# Usage:
#   DEFAULT_DB_CONNECTION=<conn> ./scripts/generate-openapi-b2b.sh
#
# A non-functional database connection is acceptable for OpenAPI export —
# the document is generated from endpoint metadata at startup, not from
# DB queries. Migrations are short-circuited via Seeding:Enabled=false.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="${REPO_ROOT}/services/backend_api/backend_api.csproj"
OUTPUT="${REPO_ROOT}/services/backend_api/openapi.b2b.json"
PORT="${PORT:-5099}"

if [[ -z "${DEFAULT_DB_CONNECTION:-}" ]]; then
  echo "ERROR: DEFAULT_DB_CONNECTION must be set (any reachable Postgres URI works for OpenAPI export)."
  exit 2
fi

echo "Starting backend_api on port ${PORT}..."
ASPNETCORE_URLS="http://localhost:${PORT}" \
  ASPNETCORE_ENVIRONMENT=Development \
  Seeding__Enabled=false \
  dotnet run --project "$PROJ" --no-build --launch-profile "" &
SERVER_PID=$!
trap "kill $SERVER_PID 2>/dev/null || true" EXIT

# Wait for /openapi/v1.json to respond.
for i in {1..30}; do
  if curl -sSf "http://localhost:${PORT}/openapi/v1.json" >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

echo "Fetching OpenAPI document..."
curl -sS "http://localhost:${PORT}/openapi/v1.json" \
  | jq '. as $doc
        | .paths
        | with_entries(select(.key
            | test("^/api/customer/quotes|^/api/admin/quotes|^/api/customer/companies|^/api/admin/companies")))
        as $b2bPaths
        | $doc | .paths = $b2bPaths' \
  > "$OUTPUT"

echo "Wrote $OUTPUT"
echo "Path count: $(jq '.paths | length' "$OUTPUT")"
