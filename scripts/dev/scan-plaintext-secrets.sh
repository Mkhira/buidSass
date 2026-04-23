#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

if [[ $# -gt 0 ]]; then
  TARGETS=("$@")
else
  TARGETS=(
    "$ROOT_DIR/services/backend_api/logs"
    "$ROOT_DIR/services/backend_api/TestResults"
  )
fi

PATTERN='(password[[:space:]]*[:=]|otp[[:space:]]*[:=]|token[[:space:]]*[:=]|authorization:[[:space:]]*bearer)'

found_files=0
violations=0

for target in "${TARGETS[@]}"; do
  if [[ -f "$target" ]]; then
    found_files=1
    if grep -Ein "$PATTERN" "$target" >/dev/null; then
      echo "[scan-plaintext-secrets] Potential plaintext secret in file: $target"
      grep -Ein "$PATTERN" "$target" || true
      violations=1
    fi
    continue
  fi

  if [[ -d "$target" ]]; then
    while IFS= read -r -d '' file; do
      found_files=1
      if grep -Ein "$PATTERN" "$file" >/dev/null; then
        echo "[scan-plaintext-secrets] Potential plaintext secret in file: $file"
        grep -Ein "$PATTERN" "$file" || true
        violations=1
      fi
    done < <(find "$target" -type f -print0)
  fi
done

if [[ $found_files -eq 0 ]]; then
  echo "[scan-plaintext-secrets] No log files found; skipping."
  exit 0
fi

if [[ $violations -ne 0 ]]; then
  echo "[scan-plaintext-secrets] FAILED"
  exit 1
fi

echo "[scan-plaintext-secrets] OK"
