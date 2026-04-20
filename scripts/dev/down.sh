#!/usr/bin/env bash
# Stop the local stack but preserve volumes.
set -euo pipefail

cd "$(dirname "$0")/../.."
docker compose -f infra/local/docker-compose.yml down
