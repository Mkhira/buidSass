#!/usr/bin/env bash
# Spec 023 T139 — generate openapi.support.json from a running app.
#
# Project uses Microsoft.AspNetCore.OpenApi (registered in Program.cs as
# `app.MapOpenApi()`) which exposes /openapi/v1.json on the dev host. This
# script boots the app on a free port, curls the OpenAPI document, filters
# down to the Support module's customer + admin paths, and writes the
# resulting per-module document to services/backend_api/openapi.support.json.
#
# Usage:
#   DEFAULT_DB_CONNECTION=<conn> ./scripts/generate-openapi-support.sh
#
# A non-functional database connection is acceptable for OpenAPI export —
# the document is generated from endpoint metadata at startup, not from DB
# queries. The migration step is short-circuited by setting
# Seeding:Enabled=false in the inline config.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="${REPO_ROOT}/services/backend_api/backend_api.csproj"
OUTPUT="${REPO_ROOT}/services/backend_api/openapi.support.json"
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
            | test("^/api/customer/support-tickets|^/api/admin/support-tickets|^/api/admin/support-policies|^/api/admin/agent-availability")))
        as $supportPaths
        | $doc | .paths = $supportPaths' \
  > "$OUTPUT"

echo "Wrote $OUTPUT"
echo "Path count: $(jq '.paths | length' "$OUTPUT")"
