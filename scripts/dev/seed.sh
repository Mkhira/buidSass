#!/usr/bin/env bash
# Run the seed CLI verb against the local backend.
# Usage: seed.sh [apply|fresh|dry-run]  (default: apply)
set -euo pipefail

MODE=${1:-apply}

cd "$(dirname "$0")/../../services/backend_api"

export ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT:-Development}
export ConnectionStrings__DefaultConnection=${ConnectionStrings__DefaultConnection:-"Host=localhost;Port=5432;Username=dental;Password=dental_dev_pw;Database=dental_commerce_dev"}

dotnet run --no-launch-profile -- seed --mode="$MODE"
