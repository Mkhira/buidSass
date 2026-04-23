#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TEST_PROJECT="$ROOT_DIR/services/backend_api/Tests/Identity.Tests/Identity.Tests.csproj"

FILTER="FullyQualifiedName~Identity.Tests.Integration.AuthorizationAuditTests|FullyQualifiedName~Identity.Tests.Integration.Customer.RegistrationAuditTests"

DOTNET_BIN="$(command -v dotnet || true)"
if [[ -z "$DOTNET_BIN" && -x "/usr/local/share/dotnet/dotnet" ]]; then
  DOTNET_BIN="/usr/local/share/dotnet/dotnet"
fi

if [[ -z "$DOTNET_BIN" ]]; then
  echo "[identity-audit-spot-check] dotnet runtime not found."
  exit 127
fi

echo "[identity-audit-spot-check] Running targeted audit integration tests..."
"$DOTNET_BIN" test "$TEST_PROJECT" --filter "$FILTER"

echo "[identity-audit-spot-check] OK"
