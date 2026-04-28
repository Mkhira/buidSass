/**
 * T032c (FR-028b): permission-catalog drift check.
 *
 * Diffs the keys declared in
 *   specs/phase-1C/015-admin-foundation/contracts/permission-catalog.md
 * against the catalog returned by spec 004's `/v1/admin/permission-catalog`
 * endpoint. Fails on any add / remove not in both surfaces.
 *
 * No-op + warning when spec 004 hasn't shipped the endpoint yet
 * (`spec-004:gap:permission-catalog-endpoint`).
 *
 * Run: `pnpm catalog:check-permissions`
 */
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const ROOT = path.resolve(path.dirname(__filename), "..");
const SPEC_FILE = path.resolve(
  ROOT,
  "../../specs/phase-1C/015-admin-foundation/contracts/permission-catalog.md",
);

function extractKeysFromMarkdown(): string[] {
  const md = readFileSync(SPEC_FILE, "utf-8");
  const keys = new Set<string>();
  // Match table cells starting with backticks containing a permission key.
  const re = /\|\s*`([a-z][a-z0-9_]*(?:\.[a-z][a-z0-9_]*)+)`/g;
  for (const m of md.matchAll(re)) keys.add(m[1]);
  return [...keys].sort();
}

async function fetchServerCatalog(): Promise<string[] | null> {
  const url = process.env.BACKEND_URL ?? "http://localhost:5000";
  try {
    const res = await fetch(`${url}/v1/admin/permission-catalog`);
    if (!res.ok) return null;
    const body = (await res.json()) as { keys?: string[] };
    return body.keys ?? null;
  } catch {
    return null;
  }
}

async function main(): Promise<void> {
  const docKeys = extractKeysFromMarkdown();
  if (docKeys.length === 0) {
    console.error("✗ permission-catalog: extracted 0 keys from contracts/permission-catalog.md");
    process.exit(1);
  }

  const serverKeys = await fetchServerCatalog();
  if (serverKeys === null) {
    console.warn(
      "⚠ permission-catalog: spec 004's /v1/admin/permission-catalog endpoint not reachable.\n" +
        "  Treating as no-op. File spec-004:gap:permission-catalog-endpoint if not yet open.\n" +
        `  Doc has ${docKeys.length} keys.`,
    );
    process.exit(0);
  }

  const docSet = new Set(docKeys);
  const serverSet = new Set(serverKeys);
  const onlyInDoc = [...docSet].filter((k) => !serverSet.has(k));
  const onlyInServer = [...serverSet].filter((k) => !docSet.has(k));

  if (onlyInDoc.length === 0 && onlyInServer.length === 0) {
    console.log(`✓ permission-catalog: ${docKeys.length} keys, in sync`);
    process.exit(0);
  }

  console.error("✗ permission-catalog drift:");
  if (onlyInDoc.length) {
    console.error(`  Only in contracts/permission-catalog.md (${onlyInDoc.length}):`);
    onlyInDoc.forEach((k) => console.error(`    + ${k}`));
  }
  if (onlyInServer.length) {
    console.error(`  Only in spec 004's catalog (${onlyInServer.length}):`);
    onlyInServer.forEach((k) => console.error(`    - ${k}`));
  }
  console.error(
    "\nFix: append the missing keys to permission-catalog.md (if the server has them) " +
      "or to spec 004's catalog (if the doc has them).",
  );
  process.exit(1);
}

main();
