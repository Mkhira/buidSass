/**
 * T022 — orders list table (Client Component).
 */
"use client";

import { useRouter } from "next/navigation";
import { useTranslations, useFormatter } from "next-intl";
import type { ColumnDef } from "@tanstack/react-table";
import { useEffect, useMemo, useState } from "react";
import { DataTable, type CursorPagination } from "@/components/data-table/data-table";
import { Input } from "@/components/ui/input";
import type { OrderListRow } from "@/lib/api/clients/orders";
import { FourStatePillRow } from "./four-state-pill-row";

export interface OrdersTableProps {
  initialData: OrderListRow[];
  initialPagination: CursorPagination;
  initialQuery: string;
}

export function OrdersTable({
  initialData,
  initialPagination,
  initialQuery,
}: OrdersTableProps) {
  const router = useRouter();
  const t = useTranslations("orders.list");
  const fmt = useFormatter();
  const [query, setQuery] = useState(initialQuery);

  const columns = useMemo<ColumnDef<OrderListRow, unknown>[]>(
    () => [
      {
        accessorKey: "number",
        header: t("table.number"),
        cell: ({ row }) => (
          <button
            className="text-left underline-offset-4 hover:underline"
            onClick={() => router.push(`/orders/${row.original.id}`)}
          >
            {row.original.number}
          </button>
        ),
      },
      {
        accessorKey: "customer",
        header: t("table.customer"),
        cell: ({ row }) =>
          row.original.b2bFlag
            ? `${row.original.customer.displayName} · ${t("b2b_chip")}`
            : row.original.customer.displayName,
      },
      {
        accessorKey: "states",
        header: t("table.states"),
        cell: ({ row }) => (
          <FourStatePillRow
            orderState={row.original.orderState}
            paymentState={row.original.paymentState}
            fulfillmentState={row.original.fulfillmentState}
            refundState={row.original.refundState}
          />
        ),
      },
      {
        accessorKey: "grandTotalMinor",
        header: t("table.total"),
        cell: ({ row }) =>
          fmt.number(row.original.grandTotalMinor / 100, {
            style: "currency",
            currency: row.original.currency,
          }),
      },
      {
        accessorKey: "placedAt",
        header: t("table.placed"),
        cell: ({ row }) =>
          fmt.dateTime(new Date(row.original.placedAt), {
            dateStyle: "medium",
            timeStyle: "short",
          }),
      },
    ],
    [t, fmt, router],
  );

  // Debounce URL sync so each keystroke doesn't trigger a server roundtrip.
  useEffect(() => {
    if (query === initialQuery) return;
    const timer = setTimeout(() => {
      const params = new URLSearchParams(window.location.search);
      if (query) params.set("q", query);
      else params.delete("q");
      router.replace(`/orders?${params.toString()}`);
    }, 250);
    return () => clearTimeout(timer);
  }, [query, initialQuery, router]);

  const filterBar = (
    <div className="flex items-center gap-ds-sm">
      <Input
        placeholder={t("search_placeholder")}
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        aria-label={t("search_placeholder")}
        className="max-w-sm"
      />
    </div>
  );

  return (
    <DataTable<OrderListRow>
      columns={columns}
      data={initialData}
      getRowId={(r) => r.id}
      pagination={initialPagination}
      filterBar={filterBar}
      disableSelection
      emptyState={{ title: t("title") }}
    />
  );
}
