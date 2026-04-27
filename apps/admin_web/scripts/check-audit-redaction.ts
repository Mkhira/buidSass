/**
 * T032g (FR-022a): audit-redaction drift check.
 *
 * Walks every audit-emission fixture under `tests/fixtures/audit/*.json`
 * and asserts each sensitive path either:
 *   (a) appears in `contracts/audit-redaction.md`'s registry, OR
 *   (b) has a documented server-side redaction note next to the fixture
 *       (a sibling `*.md` describing the policy)
 *
 * Fails on a path that's neither.
 *
 * Run: `pnpm catalog:check-audit-redaction`
 *
 * The fixtures land as each emitting spec ships its audit emission; the
 * empty-fixture-set case is allowed (no-op + console note) so this check
 * does not gate on fixtures that don't exist yet.
 */
import { readFileSync, readdirSync, existsSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const ROOT = path.resolve(path.dirname(__filename), "..");
const FIXTURES_DIR = path.resolve(ROOT, "tests/fixtures/audit");
const REDACTION_DOC = path.resolve(
  ROOT,
  "../../specs/phase-1C/015-admin-foundation/contracts/audit-redaction.md",
);

// Sensitive paths flagged by heuristic: PII patterns + free-text reason notes.
const SENSITIVE_FIELD_PATTERNS = [
  /email$/i,
  /phone$/i,
  /reasonNote$/i,
  /address(?:\.|$)/i,
  /\.recipient$/i,
  /restrictedRationale\.(ar|en)$/i,
];

/**
 * Paths that match a sensitive pattern but are intentionally NEVER
 * redacted — Constitution §25 mandates traceability for the actor of
 * an audit-emitting action.
 */
const ALWAYS_VISIBLE_PATTERNS = [/(?:^|\.)actor(?:\.|$)/];

interface RedactionRow {
  path: string;
}

function extractRegisteredPaths(): Set<string> {
  const md = readFileSync(REDACTION_DOC, "utf-8");
  const paths = new Set<string>();
  const re = /\|\s*`([a-zA-Z][a-zA-Z0-9_.\*]*)`(?:,\s*`([a-zA-Z][a-zA-Z0-9_.\*]*)`)?/g;
  for (const m of md.matchAll(re)) {
    if (m[1]) paths.add(m[1]);
    if (m[2]) paths.add(m[2]);
  }
  return paths;
}

function* walkSensitivePaths(obj: unknown, prefix: string[] = []): Generator<string> {
  if (obj && typeof obj === "object" && !Array.isArray(obj)) {
    for (const [k, v] of Object.entries(obj as Record<string, unknown>)) {
      const next = [...prefix, k];
      const dotted = next.join(".");
      if (
        SENSITIVE_FIELD_PATTERNS.some((re) => re.test(dotted)) &&
        !ALWAYS_VISIBLE_PATTERNS.some((re) => re.test(dotted))
      ) {
        yield dotted;
      }
      yield* walkSensitivePaths(v, next);
    }
  } else if (Array.isArray(obj)) {
    for (const v of obj) yield* walkSensitivePaths(v, [...prefix, "*"]);
  }
}

function pathMatches(actual: string, registered: string): boolean {
  // Treat `*` in registered as wildcard for any path segment.
  const regex = new RegExp(
    "^" +
      registered.replace(/\./g, "\\.").replace(/\\\*/g, "[^.]+").replace(/\*/g, "[^.]+") +
      "$",
  );
  return regex.test(actual);
}

function main(): void {
  if (!existsSync(FIXTURES_DIR)) {
    console.warn(
      "⚠ audit-redaction: tests/fixtures/audit/ does not exist yet — skipping (no audit emissions seeded).",
    );
    process.exit(0);
  }

  const fixtures = readdirSync(FIXTURES_DIR).filter((f) => f.endsWith(".json"));
  if (fixtures.length === 0) {
    console.warn("⚠ audit-redaction: no fixtures present — skipping.");
    process.exit(0);
  }

  const registered = extractRegisteredPaths();
  const violations: Array<{ fixture: string; sensitivePath: string }> = [];

  for (const file of fixtures) {
    const data = JSON.parse(readFileSync(path.join(FIXTURES_DIR, file), "utf-8"));
    for (const sensitive of walkSensitivePaths(data)) {
      const normalized = sensitive.replace(/^(?:before|after|metadata)\./, "");
      const isRegistered = [...registered].some((r) => pathMatches(normalized, r));
      if (!isRegistered) {
        violations.push({ fixture: file, sensitivePath: normalized });
      }
    }
  }

  if (violations.length === 0) {
    console.log(`✓ audit-redaction: ${fixtures.length} fixture(s), all sensitive paths registered`);
    process.exit(0);
  }

  console.error(`✗ audit-redaction: ${violations.length} unregistered sensitive path(s)`);
  for (const v of violations) {
    console.error(`  ${v.fixture}: ${v.sensitivePath}`);
  }
  console.error(
    "\nFix: append the path to `contracts/audit-redaction.md` with the required permission to view unredacted, OR add server-side redaction in spec 003 / 004 and document it.",
  );
  process.exit(1);
}

main();
