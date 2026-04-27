/**
 * T062 + T074b: /audit — list page (Server Component).
 *
 * Reads the filter set from URL query params (FR-021 / FR-028f deep
 * links land here pre-filtered). Defaults to last-7-days when no
 * timeframe is supplied. Server-fetches the first cursor page and
 * passes it to the Client `<AuditListTable>` for interactivity.
 */
import { getTranslations } from "next-intl/server";
import { redirect } from "next/navigation";
import { getSession } from "@/lib/auth/session";
import { hasPermission } from "@/lib/auth/permissions";
import { auditApi, type AuditFilter } from "@/lib/api/clients/audit";
import { PageHeader } from "@/components/shell/page-header";
import { AuditFilterPanel, defaultLast7Days, FILTER_KEYS } from "@/components/audit/audit-filter-panel";
import { AuditListTable } from "@/components/audit/audit-list-table";

export const dynamic = "force-dynamic";

interface AuditPageProps {
  searchParams: Record<string, string | string[] | undefined>;
}

export default async function AuditListPage({ searchParams }: AuditPageProps) {
  const session = await getSession();
  if (!session) redirect("/login");
  if (!hasPermission(session, "audit.read")) redirect("/__forbidden");

  const t = await getTranslations("audit");

  // Parse query params per T074b — pre-apply for FR-028f deep links.
  const filterMap: Record<string, string> = {};
  for (const key of FILTER_KEYS) {
    const v = searchParams[key];
    if (typeof v === "string" && v.length > 0) filterMap[key] = v;
  }
  // Default timeframe = last 7 days when neither from nor to is set.
  if (!filterMap.from && !filterMap.to) {
    const def = defaultLast7Days();
    filterMap.from = def.from;
    filterMap.to = def.to;
  }
  const cursor = typeof searchParams.cursor === "string" ? searchParams.cursor : undefined;

  const filter: AuditFilter = { ...filterMap, cursor };

  let page;
  let isErrored = false;
  try {
    page = await auditApi.list(filter);
  } catch {
    page = { entries: [], nextCursor: null };
    isErrored = true;
  }

  return (
    <div className="space-y-ds-lg">
      <PageHeader title={t("title")} />
      <AuditFilterPanel initial={filterMap} />
      <AuditListTable page={page} hasPrev={Boolean(cursor)} isErrored={isErrored} />
    </div>
  );
}
