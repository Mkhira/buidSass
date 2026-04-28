/**
 * T020 — adjust page (Server Component).
 *
 * Reads optional `?warehouse=…&sku=…` deep-link params, fetches the
 * current snapshot + reason-codes server-side, then mounts the form.
 */
import { getTranslations } from "next-intl/server";
import { requirePermission } from "@/lib/auth/guards";
import { hasPermission } from "@/lib/auth/permissions";
import { getSession } from "@/lib/auth/session";
import { PageHeader } from "@/components/shell/page-header";
import { ErrorState } from "@/components/shell/error-state";
import { AdjustForm } from "@/components/inventory/adjust/adjust-form";
import { inventoryApi } from "@/lib/api/clients/inventory";

interface SearchParams {
  warehouse?: string;
  sku?: string;
}

export default async function AdjustPage({
  searchParams,
}: {
  searchParams: SearchParams;
}) {
  await requirePermission(["inventory.adjust"], "/inventory/adjust");
  const session = await getSession();
  const t = await getTranslations("inventory.adjust");

  const initialWarehouseId = searchParams.warehouse ?? "";
  const initialSkuId = searchParams.sku ?? "";

  let onHand = 0;
  let rowVersion = 0;
  let snapshotError: string | undefined;
  let reasonCodes: Awaited<
    ReturnType<typeof inventoryApi.reasonCodes.list>
  > = [];

  try {
    reasonCodes = await inventoryApi.reasonCodes.list();
  } catch (e) {
    snapshotError = e instanceof Error ? e.message : "unknown";
  }

  if (initialSkuId && initialWarehouseId) {
    try {
      const snapshot = await inventoryApi.stock.snapshot(
        initialSkuId,
        initialWarehouseId,
      );
      onHand = snapshot.onHand;
      rowVersion = snapshot.rowVersion;
    } catch (e) {
      snapshotError = e instanceof Error ? e.message : "unknown";
    }
  }

  return (
    <div className="space-y-ds-lg">
      <PageHeader title={t("title")} />
      {snapshotError ? (
        <ErrorState reasonCode={snapshotError} />
      ) : (
        <AdjustForm
          initialWarehouseId={initialWarehouseId}
          initialSkuId={initialSkuId}
          reasonCodes={reasonCodes}
          currentOnHand={onHand}
          rowVersion={rowVersion}
          hasWriteoffBelowZeroPermission={hasPermission(
            session,
            "inventory.writeoff_below_zero",
          )}
        />
      )}
    </div>
  );
}
