#!/usr/bin/env bash
# Tail logs for one compose service (default: backend_api).
set -euo pipefail

cd "$(dirname "$0")/../.."
docker compose -f infra/local/docker-compose.yml logs -f "${1:-backend_api}"
