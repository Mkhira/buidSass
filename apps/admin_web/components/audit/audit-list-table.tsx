/**
 * T064: AuditListTable — wraps the shared <DataTable> with audit-entry
 * columns. Cursor pagination + URL-synced.
 */
"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { useTranslations, useLocale } from "next-intl";
import { useMemo } from "react";
import Link from "next/link";
import type { ColumnDef } from "@tanstack/react-table";
import type { AuditEntry, AuditPage } from "@/lib/api/clients/audit";
import { DataTable } from "@/components/data-table/data-table";
import { Badge } from "@/components/ui/badge";

export interface AuditListTableProps {
  page: AuditPage;
  hasPrev: boolean;
  /** True iff we should render the audit empty state because no rows match. */
  isErrored?: boolean;
}

export function AuditListTable({ page, hasPrev, isErrored }: AuditListTableProps) {
  const t = useTranslations("audit");
  const locale = useLocale();
  const router = useRouter();
  const searchParams = useSearchParams();

  const columns = useMemo<ColumnDef<AuditEntry>[]>(
    () => [
      {
        id: "occurredAt",
        header: t("columns.occurred_at"),
        accessorFn: (row) => row.occurredAt,
        cell: ({ row }) => (
          <span className="font-mono text-xs">
            {new Date(row.original.occurredAt).toLocaleString(locale)}
          </span>
        ),
      },
      {
        id: "actor",
        header: t("columns.actor"),
        cell: ({ row }) => <span>{row.original.actor.email ?? row.original.actor.id}</span>,
      },
      {
        id: "actionKey",
        header: t("columns.action"),
        cell: ({ row }) => (
          <Badge variant="outline" className="font-mono text-xs">
            {row.original.actionKey}
          </Badge>
        ),
      },
      {
        id: "resource",
        header: t("columns.resource"),
        cell: ({ row }) => (
          <span className="font-mono text-xs">
            {row.original.resourceType} • {row.original.resourceId}
          </span>
        ),
      },
      {
        id: "view",
        header: t("columns.view"),
        cell: ({ row }) => (
          <Link
            href={`/audit/${encodeURIComponent(row.original.id)}?${searchParams.toString()}`}
            className="text-sm text-primary hover:underline"
          >
            {t("columns.view")}
          </Link>
        ),
      },
    ],
    [t, locale, searchParams],
  );

  function goToCursor(cursor: string | null) {
    const params = new URLSearchParams(searchParams.toString());
    if (cursor) params.set("cursor", cursor);
    else params.delete("cursor");
    router.push(`/audit?${params.toString()}`);
  }

  return (
    <DataTable<AuditEntry>
      columns={columns}
      data={isErrored ? null : page.entries}
      getRowId={(row) => row.id}
      errorReason={isErrored ? "audit.list.error" : undefined}
      pagination={{ hasMore: Boolean(page.nextCursor), nextCursor: page.nextCursor }}
      hasPrevPage={hasPrev}
      onPageNext={() => goToCursor(page.nextCursor)}
      onPagePrev={() => goToCursor(null)}
      disableSelection
      emptyState={{ title: t("empty") }}
    />
  );
}
