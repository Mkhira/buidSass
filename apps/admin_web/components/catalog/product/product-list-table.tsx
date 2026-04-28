/**
 * T019 — ProductListTable.
 * Client Component wrapping spec 015's `DataTable` with catalog-specific
 * columns and a server-driven filter bar.
 */
"use client";

import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import type { ColumnDef } from "@tanstack/react-table";
import { useMemo, useState } from "react";
import { DataTable, type CursorPagination } from "@/components/data-table/data-table";
import { Input } from "@/components/ui/input";
import type { ProductSummary } from "@/lib/api/clients/catalog";
import { ProductStatePill } from "./product-state-pill";

export interface ProductListTableProps {
  initialData: ProductSummary[];
  initialPagination: CursorPagination;
  initialQuery: string;
  locale: "en" | "ar";
}

export function ProductListTable({
  initialData,
  initialPagination,
  initialQuery,
  locale,
}: ProductListTableProps) {
  const router = useRouter();
  const t = useTranslations("catalog.product");
  const [query, setQuery] = useState(initialQuery);

  const columns = useMemo<ColumnDef<ProductSummary, unknown>[]>(
    () => [
      {
        accessorKey: "sku",
        header: t("table.sku"),
        cell: ({ row }) => (
          <button
            className="text-start underline-offset-4 hover:underline"
            onClick={() => router.push(`/catalog/products/${row.original.id}`)}
          >
            {row.original.sku}
          </button>
        ),
      },
      {
        accessorKey: "name",
        header: t("table.name"),
        cell: ({ row }) => row.original.name[locale] || row.original.name.en,
      },
      {
        accessorKey: "state",
        header: t("table.state"),
        cell: ({ row }) => <ProductStatePill state={row.original.state} />,
      },
      {
        accessorKey: "restricted",
        header: t("table.restricted"),
        cell: ({ row }) => (row.original.restricted ? "✓" : ""),
      },
    ],
    [t, router, locale],
  );

  const filterBar = (
    <div className="flex items-center gap-ds-sm">
      <Input
        placeholder={t("filters.search_placeholder")}
        value={query}
        onChange={(e) => {
          const next = e.target.value;
          setQuery(next);
          const params = new URLSearchParams(window.location.search);
          if (next) params.set("q", next);
          else params.delete("q");
          router.replace(`/catalog/products?${params.toString()}`);
        }}
        aria-label={t("filters.search_placeholder")}
        className="max-w-sm"
      />
    </div>
  );

  return (
    <DataTable<ProductSummary>
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
