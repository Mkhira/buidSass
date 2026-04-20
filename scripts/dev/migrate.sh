#!/usr/bin/env bash
# Apply EF Core migrations against the compose Postgres.
set -euo pipefail

cd "$(dirname "$0")/../../services/backend_api"

export ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT:-Development}
export ConnectionStrings__DefaultConnection=${ConnectionStrings__DefaultConnection:-"Host=localhost;Port=5432;Username=dental;Password=dental_dev_pw;Database=dental_commerce_dev"}

dotnet tool restore >/dev/null 2>&1 || true
dotnet ef database update
