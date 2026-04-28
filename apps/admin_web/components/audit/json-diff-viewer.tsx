/**
 * T067 + T074a: JsonDiffViewer.
 *
 * Renders before / after JSON side-by-side with field-level redaction
 * per `redaction-policy.ts` (FR-022a defence-in-depth). Sensitive paths
 * (PII, free-text reason notes, restricted rationales) wrap their value
 * in `<MaskedField>` when the actor lacks the required permission.
 *
 * Virtualization is left to a future iteration — the audit blob is
 * typically < 5 KB; if a fixture proves otherwise the test in
 * `tests/unit/audit/json-diff-viewer.test.tsx` (T070) will surface it
 * and we'll wrap the rows in `<react-virtual>`.
 */
"use client";

import { useTranslations } from "next-intl";
import { MaskedField } from "@/components/shell/masked-field";
import { decideRedaction } from "./redaction-policy";

export interface JsonDiffViewerProps {
  before: unknown;
  after: unknown;
  /** Actor's permission set — drives field-level redaction. */
  permissions: string[];
}

export function JsonDiffViewer({ before, after, permissions }: JsonDiffViewerProps) {
  const t = useTranslations("audit.detail");
  const permSet = new Set(permissions);
  return (
    <div className="grid gap-ds-md md:grid-cols-2">
      <Pane title={t("before")} blob={before} side="before" permSet={permSet} />
      <Pane title={t("after")} blob={after} side="after" permSet={permSet} />
    </div>
  );
}

interface PaneProps {
  title: string;
  blob: unknown;
  side: "before" | "after";
  permSet: ReadonlySet<string>;
}

function Pane({ title, blob, side, permSet }: PaneProps) {
  return (
    <section className="space-y-ds-sm">
      <h3 className="text-sm font-medium text-muted-foreground">{title}</h3>
      <div
        role="region"
        aria-label={title}
        className="rounded-md border border-border bg-muted/30 p-ds-sm font-mono text-xs leading-relaxed"
      >
        <RenderBlob blob={blob} path={[side]} permSet={permSet} />
      </div>
    </section>
  );
}

interface RenderProps {
  blob: unknown;
  path: string[];
  permSet: ReadonlySet<string>;
}

function RenderBlob({ blob, path, permSet }: RenderProps) {
  if (blob === null) return <span className="text-muted-foreground">null</span>;
  if (blob === undefined) return <span className="text-muted-foreground">—</span>;
  if (typeof blob === "string" || typeof blob === "number" || typeof blob === "boolean") {
    return <Leaf value={blob} path={path} permSet={permSet} />;
  }
  if (Array.isArray(blob)) {
    return (
      <ul className="ms-ds-md list-disc">
        {blob.map((item, i) => (
          <li key={i}>
            <RenderBlob blob={item} path={[...path, "*"]} permSet={permSet} />
          </li>
        ))}
      </ul>
    );
  }
  if (typeof blob === "object") {
    const entries = Object.entries(blob as Record<string, unknown>);
    return (
      <dl className="space-y-ds-xs">
        {entries.map(([key, value]) => (
          <div key={key} className="grid grid-cols-[max-content,1fr] gap-ds-sm">
            <dt className="text-muted-foreground">{key}:</dt>
            <dd>
              <RenderBlob blob={value} path={[...path, key]} permSet={permSet} />
            </dd>
          </div>
        ))}
      </dl>
    );
  }
  return <span>{String(blob)}</span>;
}

interface LeafProps {
  value: string | number | boolean;
  path: string[];
  permSet: ReadonlySet<string>;
}

function Leaf({ value, path, permSet }: LeafProps) {
  // Reconstruct the rule-relative path: drop the leading `before` /
  // `after` / `metadata` segment.
  const fullPath = path.join(".");
  const decision = decideRedaction(fullPath, permSet);
  if (decision.redact) {
    return <MaskedField kind={decision.kind ?? "generic"} value={String(value)} canRead={false} />;
  }
  if (typeof value === "string") return <span>{value}</span>;
  if (typeof value === "number") return <span>{value}</span>;
  return <span>{String(value)}</span>;
}
