/**
 * T066: AuditEntryDetail — actor + action + timestamp + correlation id +
 * before/after JSON viewer + permalink copy.
 *
 * Field-level redaction (T074a) is handled by `<JsonDiffViewer>` —
 * sensitive paths route through `<MaskedField>` based on the actor's
 * permission set.
 */
import { getTranslations } from "next-intl/server";
import type { AuditEntry } from "@/lib/api/clients/audit";
import type { AdminSessionPayload } from "@/lib/auth/session";
import { JsonDiffViewer } from "./json-diff-viewer";
import { PermalinkCopy } from "./permalink-copy";
import { Badge } from "@/components/ui/badge";
import { MaskedField } from "@/components/shell/masked-field";

export interface AuditEntryDetailProps {
  entry: AuditEntry;
  session: AdminSessionPayload;
}

export async function AuditEntryDetail({ entry, session }: AuditEntryDetailProps) {
  const t = await getTranslations("audit");
  const canReadCustomerPii = session.permissions.includes("customers.pii.read");

  return (
    <article className="space-y-ds-lg">
      <header className="flex items-start justify-between gap-ds-md">
        <div className="space-y-ds-xs">
          <h2 className="text-xl font-semibold tracking-tight">
            <Badge variant="outline" className="font-mono">
              {entry.actionKey}
            </Badge>
          </h2>
          <p className="text-sm text-muted-foreground">
            {entry.actor.email ? (
              <MaskedField kind="email" value={entry.actor.email} canRead={canReadCustomerPii} />
            ) : (
              <span>{entry.actor.id}</span>
            )}{" "}
            • {new Date(entry.occurredAt).toLocaleString(session.roleScope === "ksa" ? "ar-SA" : "ar-EG")}
          </p>
          <p className="text-xs font-mono text-muted-foreground">
            {entry.resourceType} • {entry.resourceId}
          </p>
        </div>
        <PermalinkCopy entryId={entry.id} />
      </header>

      <dl className="grid max-w-2xl grid-cols-[max-content,1fr] gap-x-ds-md gap-y-ds-xs text-sm">
        <dt className="text-muted-foreground">{t("detail.correlation_id")}</dt>
        <dd className="font-mono text-xs" title={entry.correlationId}>
          {entry.correlationId.slice(0, 8)}…
        </dd>
        <dt className="text-muted-foreground">Market</dt>
        <dd>
          <Badge variant="secondary">{entry.marketScope}</Badge>
        </dd>
      </dl>

      <JsonDiffViewer before={entry.before} after={entry.after} permissions={session.permissions} />
    </article>
  );
}
