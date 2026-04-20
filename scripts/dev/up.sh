#!/usr/bin/env bash
# Bring up the full local stack: postgres + meilisearch + mailhog + backend_api.
# Applies migrations and runs idempotent seed.
set -euo pipefail

cd "$(dirname "$0")/../.."
COMPOSE_FILE="infra/local/docker-compose.yml"

if [ ! -f "infra/local/.env" ]; then
  echo "[up] creating infra/local/.env from .env.example"
  cp infra/local/.env.example infra/local/.env
fi

echo "[up] starting compose services..."
docker compose -f "$COMPOSE_FILE" up -d --build

echo "[up] waiting for postgres + meilisearch to be healthy..."
for svc in postgres meilisearch; do
  for _ in $(seq 1 60); do
    status=$(docker inspect -f '{{.State.Health.Status}}' "dental-local-${svc}-1" 2>/dev/null || echo "starting")
    [ "$status" = "healthy" ] && break
    sleep 1
  done
  [ "$status" = "healthy" ] || { echo "[up] $svc did not become healthy"; exit 1; }
done

echo "[up] applying migrations..."
"$(dirname "$0")/migrate.sh"

echo "[up] running seed (apply, idempotent)..."
"$(dirname "$0")/seed.sh" apply

PORT="${BACKEND_API_HOST_PORT:-5050}"
echo "[up] done. backend_api -> http://localhost:${PORT}/health"
