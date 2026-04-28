"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { useTranslations, useFormatter } from "next-intl";
import type { ColumnDef } from "@tanstack/react-table";
import { DataTable, type CursorPagination } from "@/components/data-table/data-table";
import { Input } from "@/components/ui/input";
import { MaskedField } from "@/components/shell/masked-field";
import type { CustomerListRow } from "@/lib/api/clients/customers";

export interface CustomersTableProps {
  initialData: CustomerListRow[];
  initialPagination: CursorPagination;
  initialQuery: string;
  /** Whether the admin holds customers.pii.read — controls masking. */
  canReadPii: boolean;
}

export function CustomersTable({
  initialData,
  initialPagination,
  initialQuery,
  canReadPii,
}: CustomersTableProps) {
  const router = useRouter();
  const t = useTranslations("customers.list");
  const tCustomers = useTranslations("customers");
  const tStates = useTranslations("customers.states");
  const fmt = useFormatter();
  const [query, setQuery] = useState(initialQuery);

  // Debounced URL sync — 300ms per data-model.md.
  useEffect(() => {
    if (query === initialQuery) return;
    const timer = setTimeout(() => {
      const params = new URLSearchParams(window.location.search);
      if (query) params.set("q", query);
      else params.delete("q");
      router.replace(`/customers?${params.toString()}`);
    }, 300);
    return () => clearTimeout(timer);
  }, [query, initialQuery, router]);

  const columns = useMemo<ColumnDef<CustomerListRow, unknown>[]>(
    () => [
      {
        accessorKey: "displayName",
        header: t("table.name"),
        cell: ({ row }) => (
          <button
            className="text-start underline-offset-4 hover:underline"
            onClick={() => router.push(`/customers/${row.original.id}`)}
          >
            {row.original.displayName}
            {row.original.b2bFlag ? ` · ${tCustomers("b2b_chip")}` : ""}
          </button>
        ),
      },
      {
        accessorKey: "emailMasked",
        header: t("table.email"),
        cell: ({ row }) => (
          <MaskedField
            kind="email"
            value={row.original.emailMasked}
            canRead={canReadPii}
          />
        ),
      },
      {
        accessorKey: "marketCode",
        header: t("table.market"),
        cell: ({ row }) => row.original.marketCode.toUpperCase(),
      },
      {
        accessorKey: "accountState",
        header: t("table.account_state"),
        cell: ({ row }) => {
          try {
            return tStates(row.original.accountState as never);
          } catch {
            return row.original.accountState;
          }
        },
      },
      {
        accessorKey: "lastActiveAt",
        header: t("table.last_active"),
        cell: ({ row }) =>
          fmt.dateTime(new Date(row.original.lastActiveAt), {
            dateStyle: "medium",
          }),
      },
    ],
    [t, tCustomers, tStates, fmt, router, canReadPii],
  );

  const filterBar = (
    <div className="flex items-center gap-ds-sm">
      <Input
        placeholder={t("search_placeholder")}
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        maxLength={200}
        aria-label={t("search_placeholder")}
        className="max-w-sm"
      />
    </div>
  );

  return (
    <DataTable<CustomerListRow>
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
