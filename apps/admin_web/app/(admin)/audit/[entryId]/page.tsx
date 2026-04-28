/**
 * T065: /audit/[entryId] — single-entry detail page (Server Component).
 *
 * Per FR-021, opens directly to the entry when the permalink is shared.
 * `?permalink=1` triggers a copy-confirmation toast on landing — wired
 * via a Client child below.
 */
import { getTranslations } from "next-intl/server";
import { notFound, redirect } from "next/navigation";
import Link from "next/link";
import { getSession } from "@/lib/auth/session";
import { hasPermission } from "@/lib/auth/permissions";
import { auditApi } from "@/lib/api/clients/audit";
import { isApiError } from "@/lib/api/error";
import { AuditEntryDetail } from "@/components/audit/audit-entry-detail";
import { buttonVariants } from "@/components/ui/button";
import { ArrowLeft } from "lucide-react";

export const dynamic = "force-dynamic";

interface AuditDetailPageProps {
  params: { entryId: string };
  searchParams: Record<string, string | string[] | undefined>;
}

export default async function AuditDetailPage({ params, searchParams }: AuditDetailPageProps) {
  const session = await getSession();
  if (!session) redirect("/login");
  if (!hasPermission(session, "audit.read")) redirect("/__forbidden");

  const t = await getTranslations("audit");

  let entry;
  try {
    entry = await auditApi.byId(decodeURIComponent(params.entryId));
  } catch (err) {
    if (isApiError(err) && err.status === 404) notFound();
    throw err;
  }

  // Preserve any active list filters when going back.
  const backQs = new URLSearchParams();
  for (const [k, v] of Object.entries(searchParams)) {
    if (k === "permalink") continue;
    if (typeof v === "string") backQs.set(k, v);
  }
  const backHref = backQs.toString() ? `/audit?${backQs.toString()}` : "/audit";

  return (
    <div className="space-y-ds-md">
      <Link
        href={backHref}
        className={buttonVariants({ variant: "ghost", size: "sm" })}
      >
        <ArrowLeft aria-hidden="true" className="me-ds-xs size-4 rtl:rotate-180" />
        {t("title")}
      </Link>
      <AuditEntryDetail entry={entry} session={session} />
    </div>
  );
}
