/**
 * T029 — stock-by-SKU detail page.
 * Per FR-006a, mounts <AuditForResourceLink> in the page header.
 */
import { getTranslations } from "next-intl/server";
import Link from "next/link";
import { requirePermission } from "@/lib/auth/guards";
import { hasPermission } from "@/lib/auth/permissions";
import { getSession } from "@/lib/auth/session";
import { PageHeader } from "@/components/shell/page-header";
import { AuditForResourceLink } from "@/components/shell/audit-for-resource-link";
import { ErrorState } from "@/components/shell/error-state";
import { buttonVariants } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import { inventoryApi } from "@/lib/api/clients/inventory";

export default async function StockBySkuPage({
  params,
  searchParams,
}: {
  params: { skuId: string };
  searchParams: { warehouse?: string };
}) {
  await requirePermission(
    ["inventory.read"],
    `/inventory/stock/${params.skuId}`,
  );
  const session = await getSession();
  const t = await getTranslations("inventory.stock");
  const warehouseId = searchParams.warehouse ?? "";

  let snapshot;
  let errorReason: string | undefined;
  if (warehouseId) {
    try {
      snapshot = await inventoryApi.stock.snapshot(params.skuId, warehouseId);
    } catch (e) {
      errorReason = e instanceof Error ? e.message : "unknown";
    }
  }

  return (
    <div className="space-y-ds-lg">
      <PageHeader
        title={`${t("title")} · ${params.skuId}`}
        actions={
          <>
            <Link
              href={`/inventory/adjust?sku=${encodeURIComponent(params.skuId)}&warehouse=${encodeURIComponent(warehouseId)}`}
              className={cn(buttonVariants({ variant: "default" }))}
            >
              {t("table.available")}
            </Link>
            <AuditForResourceLink
              resourceType="Sku"
              resourceId={params.skuId}
              canRead={hasPermission(session, "audit.read")}
            />
          </>
        }
      />
      {errorReason ? (
        <ErrorState reasonCode={errorReason} />
      ) : snapshot ? (
        <div className="grid gap-ds-md md:grid-cols-3">
          <div className="rounded-md border border-border bg-card p-ds-md">
            <p className="text-xs uppercase text-muted-foreground">
              {t("table.available")}
            </p>
            <p className="text-2xl font-semibold">{snapshot.available}</p>
          </div>
          <div className="rounded-md border border-border bg-card p-ds-md">
            <p className="text-xs uppercase text-muted-foreground">
              {t("table.on_hand")}
            </p>
            <p className="text-2xl font-semibold">{snapshot.onHand}</p>
          </div>
          <div className="rounded-md border border-border bg-card p-ds-md">
            <p className="text-xs uppercase text-muted-foreground">
              {t("table.reserved")}
            </p>
            <p className="text-2xl font-semibold">{snapshot.reserved}</p>
          </div>
        </div>
      ) : null}
    </div>
  );
}
