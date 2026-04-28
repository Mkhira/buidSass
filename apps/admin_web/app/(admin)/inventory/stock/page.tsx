/**
 * T028 — stock list (Server Component).
 *
 * Renders the cross-warehouse stock snapshot list. Subsequent
 * interactions (search / pagination / warehouse filter) are owned by
 * the Client Component table; for v1 we render a simple read-only list.
 */
import { getTranslations } from "next-intl/server";
import Link from "next/link";
import { requirePermission } from "@/lib/auth/guards";
import { PageHeader } from "@/components/shell/page-header";
import { ErrorState } from "@/components/shell/error-state";
import { EmptyState } from "@/components/shell/empty-state";
import { inventoryApi, type StockSnapshot } from "@/lib/api/clients/inventory";

export default async function StockListPage({
  searchParams,
}: {
  searchParams: { q?: string; warehouse?: string };
}) {
  await requirePermission(["inventory.read"], "/inventory/stock");
  const t = await getTranslations("inventory.stock");

  let rows: StockSnapshot[] = [];
  let errorReason: string | undefined;
  try {
    const page = await inventoryApi.stock.listBySku({
      search: searchParams.q,
      warehouseId: searchParams.warehouse,
    });
    rows = page.rows;
  } catch (e) {
    errorReason = e instanceof Error ? e.message : "unknown";
  }

  return (
    <div className="space-y-ds-lg">
      <PageHeader title={t("title")} />
      {errorReason ? (
        <ErrorState reasonCode={errorReason} />
      ) : rows.length === 0 ? (
        <EmptyState title={t("title")} />
      ) : (
        <div className="overflow-x-auto rounded-md border border-border">
          <table className="min-w-full text-sm">
            <thead className="bg-muted/30">
              <tr>
                <th className="p-ds-sm text-start">{t("table.sku")}</th>
                <th className="p-ds-sm text-start">{t("table.warehouse")}</th>
                <th className="p-ds-sm text-end">{t("table.available")}</th>
                <th className="p-ds-sm text-end">{t("table.on_hand")}</th>
                <th className="p-ds-sm text-end">{t("table.reserved")}</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={`${r.skuId}-${r.warehouseId}`}>
                  <td className="p-ds-sm">
                    <Link
                      href={`/inventory/stock/${encodeURIComponent(r.skuId)}?warehouse=${encodeURIComponent(r.warehouseId)}`}
                      className="underline-offset-4 hover:underline"
                    >
                      {r.skuId}
                    </Link>
                  </td>
                  <td className="p-ds-sm">{r.warehouseId}</td>
                  <td className="p-ds-sm text-end">{r.available}</td>
                  <td className="p-ds-sm text-end">{r.onHand}</td>
                  <td className="p-ds-sm text-end">{r.reserved}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
