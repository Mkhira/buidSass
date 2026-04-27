/**
 * T032e (FR-028g): nav-manifest validator.
 *
 * Walks every JSON contribution under `lib/auth/nav-manifest-static/` and
 * asserts:
 *  - group / entry ids are unique across modules
 *  - every `labelKey` resolves in both `messages/{en,ar}.json`
 *  - every `requiredPermissions` key exists in `contracts/permission-catalog.md`
 *  - every `order` falls within the module's reserved range
 *
 * Run: `pnpm catalog:check-nav-manifest`
 */
import { readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const ROOT = path.resolve(path.dirname(__filename), "..");
const STATIC_DIR = path.resolve(ROOT, "lib/auth/nav-manifest-static");
const EN_PATH = path.resolve(ROOT, "messages/en.json");
const AR_PATH = path.resolve(ROOT, "messages/ar.json");
const CATALOG_PATH = path.resolve(
  ROOT,
  "../../specs/phase-1C/015-admin-foundation/contracts/permission-catalog.md",
);

const RESERVED_RANGES: Record<string, [number, number]> = {
  foundation: [100, 199],
  catalog: [200, 299],
  inventory: [300, 399],
  orders: [400, 499],
  customers: [500, 599],
  verification: [600, 699],
  b2b: [700, 799],
  cms: [800, 899],
  support: [900, 999],
};

interface Entry {
  id: string;
  labelKey: string;
  iconKey?: string;
  route: string;
  requiredPermissions: string[];
  order: number;
  badgeCountKey?: string | null;
}

interface Group {
  groupId: string;
  labelKey: string;
  iconKey?: string;
  order: number;
  entries: Entry[];
}

function extractCatalogKeys(): Set<string> {
  const md = readFileSync(CATALOG_PATH, "utf-8");
  const keys = new Set<string>();
  const re = /\|\s*`([a-z][a-z0-9_]*(?:\.[a-z][a-z0-9_]*)+)`/g;
  for (const m of md.matchAll(re)) keys.add(m[1]);
  return keys;
}

function flattenKeys(obj: unknown, prefix: string[] = [], out = new Set<string>()): Set<string> {
  if (obj && typeof obj === "object") {
    for (const [k, v] of Object.entries(obj as Record<string, unknown>)) {
      if (k.startsWith("@@")) continue;
      const next = [...prefix, k];
      if (v && typeof v === "object") flattenKeys(v, next, out);
      else out.add(next.join("."));
    }
  }
  return out;
}

function main(): void {
  const errors: string[] = [];
  const seenIds = new Set<string>();

  const enKeys = flattenKeys(JSON.parse(readFileSync(EN_PATH, "utf-8")));
  const arKeys = flattenKeys(JSON.parse(readFileSync(AR_PATH, "utf-8")));
  const catalogKeys = extractCatalogKeys();

  const files = readdirSync(STATIC_DIR).filter((f) => f.endsWith(".json"));
  if (files.length === 0) {
    console.error("✗ nav-manifest: no contribution files under lib/auth/nav-manifest-static/");
    process.exit(1);
  }

  for (const file of files) {
    const moduleName = file.replace(/\.json$/, "");
    const range = RESERVED_RANGES[moduleName];
    if (!range) {
      errors.push(`${file}: module name "${moduleName}" has no reserved range in scripts/check-nav-manifest.ts`);
      continue;
    }
    const group = JSON.parse(readFileSync(path.join(STATIC_DIR, file), "utf-8")) as Group;

    if (seenIds.has(group.groupId)) errors.push(`${file}: duplicate groupId "${group.groupId}"`);
    seenIds.add(group.groupId);

    if (group.order < range[0] || group.order > range[1]) {
      errors.push(`${file}: group.order ${group.order} outside reserved range ${range[0]}-${range[1]}`);
    }

    // group label key — convention: "nav.group.<groupId>". Allow custom but warn.
    if (!enKeys.has(group.labelKey)) {
      errors.push(`${file}: labelKey "${group.labelKey}" missing in en.json`);
    }
    if (!arKeys.has(group.labelKey)) {
      errors.push(`${file}: labelKey "${group.labelKey}" missing in ar.json`);
    }

    for (const entry of group.entries) {
      const entryFqId = `${group.groupId}.${entry.id}`;
      if (seenIds.has(entryFqId)) errors.push(`${file}: duplicate entry id "${entryFqId}"`);
      seenIds.add(entryFqId);

      if (entry.order < range[0] || entry.order > range[1]) {
        errors.push(`${file}: entry "${entry.id}" order ${entry.order} outside reserved range`);
      }
      if (!enKeys.has(entry.labelKey)) {
        errors.push(`${file}: entry "${entry.id}" labelKey "${entry.labelKey}" missing in en.json`);
      }
      if (!arKeys.has(entry.labelKey)) {
        errors.push(`${file}: entry "${entry.id}" labelKey "${entry.labelKey}" missing in ar.json`);
      }
      for (const key of entry.requiredPermissions) {
        if (!catalogKeys.has(key)) {
          errors.push(`${file}: entry "${entry.id}" references unknown permission "${key}"`);
        }
      }
    }
  }

  if (errors.length === 0) {
    console.log(`✓ nav-manifest: ${files.length} module contribution(s), in sync`);
    process.exit(0);
  }

  console.error(`✗ nav-manifest: ${errors.length} error(s)`);
  for (const e of errors) console.error(`  ${e}`);
  process.exit(1);
}

main();
