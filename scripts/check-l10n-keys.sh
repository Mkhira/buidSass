#!/usr/bin/env bash
set -euo pipefail

EN_FILE="${1:-packages/design_system/lib/l10n/app_en.arb}"
AR_FILE="${2:-packages/design_system/lib/l10n/app_ar.arb}"

node - "${EN_FILE}" "${AR_FILE}" <<'NODE'
const fs = require('fs');
const enPath = process.argv[2];
const arPath = process.argv[3];
const en = JSON.parse(fs.readFileSync(enPath, 'utf8'));
const ar = JSON.parse(fs.readFileSync(arPath, 'utf8'));

const enKeys = Object.keys(en).filter(k => !k.startsWith('@'));
const arKeys = new Set(Object.keys(ar).filter(k => !k.startsWith('@')));

const missing = enKeys.filter(k => !arKeys.has(k));
if (missing.length > 0) {
  console.error('Missing AR localization keys:', missing.join(', '));
  process.exit(1);
}

console.log('Localization key parity check passed.');
NODE
