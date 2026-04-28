/**
 * T032f (FR-028e): no-locale-leaky-cache lint.
 *
 * Walks every `useQuery` / `useSuspenseQuery` call (and `lib/api/clients/*`
 * wrapper hooks) and rejects the build when:
 *   - the inferred URL matches an i18n-bearing endpoint registered in
 *     `contracts/locale-aware-endpoints.md`, AND
 *   - the query key array does NOT include `useLocale()` (or a literal
 *     locale string in tests).
 *
 * Run: `pnpm lint:cache-locale`
 */
import { Project, Node, SyntaxKind, type CallExpression } from "ts-morph";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const ROOT = path.resolve(path.dirname(__filename), "..", "..");
const REGISTRY_FILE = path.resolve(
  ROOT,
  "../../specs/phase-1C/015-admin-foundation/contracts/locale-aware-endpoints.md",
);

const TARGET_GLOBS = ["app/**/*.{ts,tsx}", "components/**/*.{ts,tsx}", "lib/**/*.{ts,tsx}"];
const EXCLUDED = ["lib/api/types/**", "**/*.test.{ts,tsx}", "**/*.stories.{ts,tsx}"];

function extractLocaleAwareEndpoints(): RegExp[] {
  const md = readFileSync(REGISTRY_FILE, "utf-8");
  const endpoints: string[] = [];
  // Match `GET /v1/...` or `POST /v1/...` patterns inside table cells.
  const re = /\|\s*`(?:GET|POST|PUT|DELETE|PATCH)\s+(\/v1\/[a-zA-Z0-9_/{}*:.-]+)`/g;
  for (const m of md.matchAll(re)) endpoints.push(m[1]);

  // Convert `:id` / `{id}` / `*` placeholders to regex wildcards.
  return endpoints.map(
    (ep) =>
      new RegExp(
        "^" +
          ep
            .replace(/[.+?^${}()|[\]\\]/g, "\\$&")
            .replace(/\\\{[^}]+\\\}/g, "[^/]+")
            .replace(/:[a-zA-Z0-9_]+/g, "[^/]+")
            .replace(/\\\*/g, ".*") +
          "$",
      ),
  );
}

function minimatch(pathStr: string, glob: string): boolean {
  const re = new RegExp(
    "^" +
      glob
        .replace(/\./g, "\\.")
        .replace(/\*\*/g, "@@DOUBLESTAR@@")
        .replace(/\*/g, "[^/]*")
        .replace(/@@DOUBLESTAR@@/g, ".*")
        .replace(/\?/g, ".")
        .replace(/\{([^}]+)\}/g, (_, alts: string) => `(${alts.split(",").join("|")})`) +
      "$",
  );
  return re.test(pathStr);
}

function findUrlInCallChain(call: CallExpression): string | null {
  // Walk the function body to find `proxyFetch("/v1/...")` calls.
  const expr = call.getExpression();
  const text = expr.getText();
  if (!/(?:useQuery|useSuspenseQuery)$/.test(text)) return null;
  const queryFn = call.getArguments()[0];
  if (!queryFn || !Node.isObjectLiteralExpression(queryFn)) return null;
  const queryFnProp = queryFn.getProperty("queryFn");
  if (!queryFnProp) return null;
  const text2 = queryFnProp.getText();
  const urlMatch = text2.match(/(["'`])(\/v1\/[a-zA-Z0-9_/{}*:.-]+)\1/);
  return urlMatch ? urlMatch[2] : null;
}

function keyArrayIncludesLocale(call: CallExpression): boolean {
  const queryFn = call.getArguments()[0];
  if (!queryFn || !Node.isObjectLiteralExpression(queryFn)) return false;
  const keyProp = queryFn.getProperty("queryKey");
  if (!keyProp) return false;
  const text = keyProp.getText();
  return /useLocale\s*\(\s*\)|\blocale\b/.test(text);
}

interface Violation {
  file: string;
  line: number;
  url: string;
}

function main(): void {
  const tsconfigPath = path.join(ROOT, "tsconfig.json");
  const project = new Project({ tsConfigFilePath: tsconfigPath, skipAddingFilesFromTsConfig: false });
  const endpointPatterns = extractLocaleAwareEndpoints();
  const violations: Violation[] = [];

  for (const sourceFile of project.getSourceFiles()) {
    const filePath = path.relative(ROOT, sourceFile.getFilePath());
    if (EXCLUDED.some((p) => minimatch(filePath, p))) continue;
    if (!TARGET_GLOBS.some((p) => minimatch(filePath, p))) continue;

    sourceFile.forEachDescendant((node) => {
      if (node.getKind() !== SyntaxKind.CallExpression) return;
      const call = node as CallExpression;
      const url = findUrlInCallChain(call);
      if (!url) return;
      const isI18nBearing = endpointPatterns.some((re) => re.test(url));
      if (!isI18nBearing) return;
      if (keyArrayIncludesLocale(call)) return;
      const { line } = sourceFile.getLineAndColumnAtPos(call.getStart());
      violations.push({ file: filePath, line, url });
    });
  }

  if (violations.length === 0) {
    console.log("✓ no-locale-leaky-cache: clean");
    process.exit(0);
  }

  console.error(`✗ no-locale-leaky-cache: ${violations.length} violation(s)`);
  for (const v of violations) {
    console.error(`  ${v.file}:${v.line}  '${v.url}' carries server-localized strings; include useLocale() in the queryKey.`);
  }
  console.error(
    "\nSee specs/phase-1C/015-admin-foundation/contracts/locale-aware-endpoints.md for the registry.",
  );
  process.exit(1);
}

main();
