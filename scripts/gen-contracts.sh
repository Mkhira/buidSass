#!/usr/bin/env bash
set -euo pipefail

# Usage: ./scripts/gen-contracts.sh [openapi-file]
# Generates shared contracts for .NET, Dart, and TypeScript.

OPENAPI_FILE="${1:-packages/shared_contracts/openapi.json}"
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_DIR="${ROOT_DIR}/packages/shared_contracts/dotnet"
DART_DIR="${ROOT_DIR}/packages/shared_contracts/dart"
TS_DIR="${ROOT_DIR}/packages/shared_contracts/typescript"
TMP_DIR="${ROOT_DIR}/.tmp/contracts"

if [ ! -f "${ROOT_DIR}/${OPENAPI_FILE}" ] && [ ! -f "${OPENAPI_FILE}" ]; then
  echo "OpenAPI file not found: ${OPENAPI_FILE}" >&2
  exit 1
fi

if [ -f "${ROOT_DIR}/${OPENAPI_FILE}" ]; then
  OPENAPI_PATH="${ROOT_DIR}/${OPENAPI_FILE}"
else
  OPENAPI_PATH="${OPENAPI_FILE}"
fi

mkdir -p "${DOTNET_DIR}" "${DART_DIR}" "${TS_DIR}" "${TMP_DIR}"

VERSION="$(
  node -e "const fs=require('fs');const p=process.argv[1];const doc=JSON.parse(fs.readFileSync(p,'utf8'));process.stdout.write((doc.info&&doc.info.version)||'0.0.0');" "${OPENAPI_PATH}"
)"

echo "Using OpenAPI: ${OPENAPI_PATH}"
echo "Detected version: ${VERSION}"

# Keep package manifests aligned to OpenAPI semver.
# Use python for portable in-place edits — GNU vs BSD sed -i differ on macOS.
python3 - "${VERSION}" "${DOTNET_DIR}/SharedContracts.csproj" "${DART_DIR}/pubspec.yaml" "${TS_DIR}/package.json" <<'PY'
import re, sys, pathlib
version, csproj, pubspec, pkg = sys.argv[1:5]
p = pathlib.Path(csproj); p.write_text(re.sub(r"<Version>[^<]+</Version>", f"<Version>{version}</Version>", p.read_text()))
p = pathlib.Path(pubspec); p.write_text(re.sub(r"^version: .*", f"version: {version}", p.read_text(), flags=re.M))
p = pathlib.Path(pkg);     p.write_text(re.sub(r'"version"\s*:\s*"[^"]*"', f'"version": "{version}"', p.read_text()))
PY

# .NET contracts (Kiota)
if command -v kiota >/dev/null 2>&1; then
  kiota generate \
    --language csharp \
    --class-name DentalCommerceApiClient \
    --namespace-name BuidSass.SharedContracts \
    --output "${DOTNET_DIR}/Generated" \
    --openapi "${OPENAPI_PATH}"
else
  mkdir -p "${DOTNET_DIR}/Generated"
  cat > "${DOTNET_DIR}/Generated/DentalCommerceApiClient.cs" <<'CS'
namespace BuidSass.SharedContracts;

public sealed class DentalCommerceApiClient
{
    public string Source => "fallback";
}
CS
fi

# Dart contracts (openapi-generator dart-dio)
if command -v openapi-generator >/dev/null 2>&1; then
  openapi-generator generate \
    -g dart-dio \
    -i "${OPENAPI_PATH}" \
    -o "${DART_DIR}/generated" \
    --additional-properties=pubName=shared_contracts,pubVersion="${VERSION}"
else
  mkdir -p "${DART_DIR}/lib"
  cat > "${DART_DIR}/lib/shared_contracts.dart" <<'DART'
class SharedContractsApi {
  const SharedContractsApi();
}
DART
fi

# TypeScript contracts (openapi-typescript)
if command -v openapi-typescript >/dev/null 2>&1; then
  openapi-typescript "${OPENAPI_PATH}" --output "${TS_DIR}/types.ts"
else
  npx -y openapi-typescript "${OPENAPI_PATH}" --output "${TS_DIR}/types.ts"
fi

echo "Generated contracts:"
echo "  - ${DOTNET_DIR}"
echo "  - ${DART_DIR}"
echo "  - ${TS_DIR}"
