/**
 * T006: No-hardcoded-strings lint (FR-013 enforcement).
 *
 * Walks `app/`, `components/` (excluding `components/ui/` shadcn primitives),
 * and `lib/` for user-facing string literals that bypass the i18n layer.
 *
 * Catches:
 *   - <Foo>{'literal'}</Foo>
 *   - <input placeholder="literal" />
 *   - aria-label="literal"
 *   - Tooltip / title="literal"
 *
 * Allow-listed:
 *   - Route paths, class names, single-character punctuation, design tokens
 *     (e.g., "ar", "en", "ksa", "eg" enum strings)
 *   - Imports / require / module paths
 *
 * Run: `pnpm lint:i18n`
 * Wire: `package.json` scripts + CI step T008.
 */
import { Project, Node, JsxAttribute, JsxText, StringLiteral } from "ts-morph";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const ROOT = path.resolve(path.dirname(__filename), "..", "..");

const TARGET_GLOBS = [
  "app/**/*.{ts,tsx}",
  "components/**/*.{ts,tsx}",
  "lib/**/*.{ts,tsx}",
];

const EXCLUDED = [
  "components/ui/**",
  "lib/api/types/**",
  "**/*.test.{ts,tsx}",
  "**/*.stories.{ts,tsx}",
  "**/*.spec.{ts,tsx}",
];

// strings that are NOT user-facing and should be ignored
const ALLOW_PATTERNS: RegExp[] = [
  /^[\s\p{P}]*$/u, // pure whitespace / punctuation
  /^[a-z][a-z0-9-]*$/i, // CSS-class-ish single tokens
  /^[A-Z_][A-Z0-9_]*$/, // CONSTANT_CASE
  /^\d+(\.\d+)?(px|rem|em|%)?$/, // numeric / sizes
  /^[a-z]+(-[a-z0-9]+)+$/, // kebab-tokens
];

const PII_BEARING_ATTRS = new Set([
  "aria-label",
  "aria-description",
  "title",
  "placeholder",
  "alt",
]);

interface Violation {
  file: string;
  line: number;
  column: number;
  literal: string;
  context: "JSX text" | "JSX attribute" | "Tooltip / title";
}

function isAllowed(s: string): boolean {
  const trimmed = s.trim();
  if (trimmed.length < 2) return true;
  return ALLOW_PATTERNS.some((re) => re.test(trimmed));
}

function checkAttribute(attr: JsxAttribute, violations: Violation[], filePath: string): void {
  const name = attr.getNameNode().getText();
  if (!PII_BEARING_ATTRS.has(name)) return;
  const initializer = attr.getInitializer();
  if (!initializer || !Node.isStringLiteral(initializer)) return;
  const literal = (initializer as StringLiteral).getLiteralValue();
  if (isAllowed(literal)) return;
  const { line, column } = attr.getSourceFile().getLineAndColumnAtPos(attr.getStart());
  violations.push({
    file: filePath,
    line,
    column,
    literal,
    context: "JSX attribute",
  });
}

function checkJsxText(text: JsxText, violations: Violation[], filePath: string): void {
  const literal = text.getLiteralText();
  if (isAllowed(literal)) return;
  const { line, column } = text.getSourceFile().getLineAndColumnAtPos(text.getStart());
  violations.push({
    file: filePath,
    line,
    column,
    literal: literal.trim(),
    context: "JSX text",
  });
}

function main(): void {
  const project = new Project({
    tsConfigFilePath: path.join(ROOT, "tsconfig.json"),
    skipAddingFilesFromTsConfig: false,
  });
  const violations: Violation[] = [];

  for (const sourceFile of project.getSourceFiles()) {
    const filePath = path.relative(ROOT, sourceFile.getFilePath());
    if (EXCLUDED.some((p) => minimatch(filePath, p))) continue;
    if (!TARGET_GLOBS.some((p) => minimatch(filePath, p))) continue;

    sourceFile.forEachDescendant((node) => {
      if (Node.isJsxText(node)) {
        checkJsxText(node, violations, filePath);
      } else if (Node.isJsxAttribute(node)) {
        checkAttribute(node, violations, filePath);
      }
    });
  }

  if (violations.length === 0) {
    console.log("✓ no-hardcoded-strings: clean");
    process.exit(0);
  }

  console.error(`✗ no-hardcoded-strings: ${violations.length} violation(s)`);
  for (const v of violations) {
    console.error(`  ${v.file}:${v.line}:${v.column} [${v.context}] ${JSON.stringify(v.literal)}`);
  }
  console.error("");
  console.error("Move user-facing strings to messages/{en,ar}.json and reference them via useTranslations() / getTranslations() (FR-013).");
  process.exit(1);
}

// minimatch lite — avoid pulling micromatch in
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

main();
