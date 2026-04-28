/**
 * T021 — orders list (Server Component).
 */
import { getTranslations } from "next-intl/server";
import { requirePermission } from "@/lib/auth/guards";
import { ordersApi, type OrderListRow } from "@/lib/api/clients/orders";
import { PageHeader } from "@/components/shell/page-header";
import { ErrorState } from "@/components/shell/error-state";
import { OrdersTable } from "@/components/orders/list/orders-table";

interface SearchParams {
  q?: string;
  cursor?: string;
}

export default async function OrdersListPage({
  searchParams,
}: {
  searchParams: SearchParams;
}) {
  await requirePermission(["orders.read"], "/orders");
  const t = await getTranslations("orders.list");

  let rows: OrderListRow[] = [];
  let nextCursor: string | null = null;
  let errorReason: string | undefined;
  try {
    const page = await ordersApi.list({
      searchQuery: searchParams.q ?? null,
      cursor: searchParams.cursor,
    });
    rows = page.rows;
    nextCursor = page.nextCursor;
  } catch (e) {
    errorReason = e instanceof Error ? e.message : "unknown";
  }

  return (
    <div className="space-y-ds-lg">
      <PageHeader title={t("title")} />
      {errorReason ? (
        <ErrorState reasonCode={errorReason} />
      ) : (
        <OrdersTable
          initialData={rows}
          initialPagination={{
            hasMore: Boolean(nextCursor),
            nextCursor,
          }}
          initialQuery={searchParams.q ?? ""}
        />
      )}
    </div>
  );
}
