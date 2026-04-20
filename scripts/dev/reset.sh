#!/usr/bin/env bash
# Tear down the local stack WITH volumes (destroys DB + Meilisearch data),
# then bring everything back up with a fresh seed.
set -euo pipefail

cd "$(dirname "$0")/../.."

echo "[reset] stopping services and removing volumes..."
docker compose -f infra/local/docker-compose.yml down -v

"$(dirname "$0")/up.sh"
