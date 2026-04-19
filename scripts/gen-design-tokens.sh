#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_FILE="${ROOT_DIR}/packages/design_system/tokens.css"

cat > "${OUT_FILE}" <<'CSS'
:root {
  --color-primary: #1F6F5F;
  --color-secondary: #2FA084;
  --color-accent: #6FCF97;
  --color-neutral: #EEEEEE;

  --spacing-xs: 4px;
  --spacing-sm: 8px;
  --spacing-md: 16px;
  --spacing-lg: 24px;
  --spacing-xl: 32px;
}
CSS

echo "Wrote ${OUT_FILE}"
