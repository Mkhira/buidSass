/**
 * T007: No-physical-margins lint (RTL hygiene).
 *
 * Rejects Tailwind utilities that bake LTR/RTL direction into the markup —
 * forcing the team to use logical-property utilities instead. Tailwind 3.4
 * supports `ms-`, `me-`, `ps-`, `pe-`, `start-`, `end-`, `text-start`,
 * `text-end` which auto-flip with `dir`.
 *
 * Catches (in `className=` strings):
 *   - mr-* / ml-*
 *   - pr-* / pl-*
 *   - text-left / text-right
 *   - left-* / right-*
 *
 * Allow-listed:
 *   - components/ui/**  (shadcn vendored primitives — direction-agnostic)
 *
 * Run: `pnpm lint:rtl`
 * Wire: `package.json` scripts + CI step T008.
 */
import { Project, Node, StringLiteral } from "ts-morph";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const ROOT = path.resolve(path.dirname(__filename), "..", "..");

const TARGET_GLOBS = ["app/**/*.tsx", "components/**/*.tsx", "lib/**/*.tsx"];
const EXCLUDED = ["components/ui/**", "**/*.test.tsx", "**/*.stories.tsx"];

const PHYSICAL_PATTERN = /\b(?:mr|ml|pr|pl|text-left|text-right|left|right)(?:-[a-zA-Z0-9.\/]+)?\b/g;
const REPLACEMENTS: Record<string, string> = {
  "mr-": "me-",
  "ml-": "ms-",
  "pr-": "pe-",
  "pl-": "ps-",
  "text-left": "text-start",
  "text-right": "text-end",
  "left-": "start-",
  "right-": "end-",
};

interface Violation {
  file: string;
  line: number;
  column: number;
  match: string;
  suggestion: string;
}

function suggest(match: string): string {
  for (const [physical, logical] of Object.entries(REPLACEMENTS)) {
    if (match.startsWith(physical)) return match.replace(physical, logical);
    if (match === physical.replace(/-$/, "")) return logical;
  }
  return match;
}

function minimatch(pathStr: string, glob: string): boolean {
  const re = new RegExp(
    "^" +
      glob
        .replace(/\./g, "\\.")
        .replace(/\*\*/g, "@@DOUBLESTAR@@")
        .replace(/\*/g, "[^/]*")
        .replace(/@@DOUBLESTAR@@/g, ".*") +
      "$",
  );
  return re.test(pathStr);
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
      if (!Node.isJsxAttribute(node)) return;
      if (node.getNameNode().getText() !== "className") return;
      const initializer = node.getInitializer();
      if (!initializer || !Node.isStringLiteral(initializer)) return;
      const value = (initializer as StringLiteral).getLiteralValue();
      const matches = value.match(PHYSICAL_PATTERN);
      if (!matches) return;
      for (const match of matches) {
        const { line, column } = sourceFile.getLineAndColumnAtPos(initializer.getStart());
        violations.push({
          file: filePath,
          line,
          column,
          match,
          suggestion: suggest(match),
        });
      }
    });
  }

  if (violations.length === 0) {
    console.log("✓ no-physical-margins: clean");
    process.exit(0);
  }

  console.error(`✗ no-physical-margins: ${violations.length} violation(s)`);
  for (const v of violations) {
    console.error(`  ${v.file}:${v.line}:${v.column}  ${v.match}  →  ${v.suggestion}`);
  }
  console.error("");
  console.error("Use logical-property utilities so AR-RTL flips automatically. shadcn primitives in components/ui/ are exempt.");
  process.exit(1);
}

main();
