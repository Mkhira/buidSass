/**
 * T041: DataTable (FR-023).
 *
 * Generic table wrapper over `@tanstack/react-table` with:
 *  - server-pagination (cursor-based)
 *  - column-driven filters
 *  - sortable columns
 *  - row selection + bulk actions slot
 *  - empty / loading / error states
 *  - saved views slot (consumed via `<SavedViewsBar>` from
 *    `./saved-views.tsx`)
 *
 * Feature pages compose `<DataTable columns={…} data={…} />`. The
 * component is intentionally generic — feature-specific filtering UI is
 * passed via the `<filterBar>` slot.
 */
"use client";

import {
  flexRender,
  getCoreRowModel,
  getSortedRowModel,
  useReactTable,
  type ColumnDef,
  type SortingState,
  type RowSelectionState,
} from "@tanstack/react-table";
import { useState, type ReactNode } from "react";
import { useTranslations } from "next-intl";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { LoadingState } from "@/components/shell/loading-state";
import { EmptyState } from "@/components/shell/empty-state";
import { ErrorState } from "@/components/shell/error-state";
import { ChevronLeft, ChevronRight, ChevronsUpDown } from "lucide-react";

export interface CursorPagination {
  hasMore: boolean;
  nextCursor: string | null;
  /** Optional total count (only when the backend cheaply has it). */
  totalCount?: number;
}

export interface DataTableProps<TRow> {
  columns: ColumnDef<TRow, unknown>[];
  data: TRow[] | null;
  /** Stable id resolver — used for selection + key. */
  getRowId: (row: TRow) => string;
  /** Set true while loading the first page. */
  isLoading?: boolean;
  /** Set true on a fetch error to render `<ErrorState>`. */
  errorReason?: string;
  /** Cursor-pagination metadata; absent = single page. */
  pagination?: CursorPagination;
  onPageNext?: () => void;
  onPagePrev?: () => void;
  hasPrevPage?: boolean;
  /** Filter / search bar mounted above the table. */
  filterBar?: ReactNode;
  /** Bulk-action bar rendered when selection is non-empty. */
  renderBulkActions?: (selectedIds: string[]) => ReactNode;
  /** Per-spec saved-views control mounted top-right. */
  savedViewsBar?: ReactNode;
  /** Disable selection (FR-001 of 018/019 — bulk explicitly out of v1). */
  disableSelection?: boolean;
  /** Empty-state title / body / action. */
  emptyState?: { title?: string; body?: string; action?: ReactNode };
}

export function DataTable<TRow>(props: DataTableProps<TRow>) {
  const t = useTranslations("shell");
  const [sorting, setSorting] = useState<SortingState>([]);
  const [rowSelection, setRowSelection] = useState<RowSelectionState>({});

  const table = useReactTable({
    data: props.data ?? [],
    columns: props.columns,
    getRowId: props.getRowId,
    state: { sorting, rowSelection },
    onSortingChange: setSorting,
    onRowSelectionChange: props.disableSelection ? undefined : setRowSelection,
    enableRowSelection: !props.disableSelection,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
    manualPagination: true,
  });

  const selectedIds = Object.keys(rowSelection);

  return (
    <div className="space-y-ds-md">
      <div className="flex items-center justify-between gap-ds-md">
        <div className="flex-1">{props.filterBar}</div>
        {props.savedViewsBar ? <div>{props.savedViewsBar}</div> : null}
      </div>

      {!props.disableSelection && selectedIds.length > 0 && props.renderBulkActions ? (
        <div className="flex items-center justify-between rounded-md border border-border bg-muted/30 p-ds-sm">
          <span className="text-sm">
            {selectedIds.length} selected
          </span>
          <div className="flex items-center gap-ds-sm">{props.renderBulkActions(selectedIds)}</div>
        </div>
      ) : null}

      {props.errorReason ? (
        <ErrorState reasonCode={props.errorReason} />
      ) : props.isLoading ? (
        <LoadingState rows={6} />
      ) : props.data && props.data.length === 0 ? (
        <EmptyState
          title={props.emptyState?.title}
          body={props.emptyState?.body}
          action={props.emptyState?.action}
        />
      ) : (
        <div className="rounded-md border border-border">
          <Table>
            <TableHeader>
              {table.getHeaderGroups().map((headerGroup) => (
                <TableRow key={headerGroup.id}>
                  {headerGroup.headers.map((header) => (
                    <TableHead key={header.id}>
                      {header.isPlaceholder ? null : header.column.getCanSort() ? (
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={header.column.getToggleSortingHandler()}
                          className="-ms-ds-sm"
                        >
                          {flexRender(header.column.columnDef.header, header.getContext())}
                          <ChevronsUpDown aria-hidden="true" className="ms-ds-xs size-3" />
                        </Button>
                      ) : (
                        flexRender(header.column.columnDef.header, header.getContext())
                      )}
                    </TableHead>
                  ))}
                </TableRow>
              ))}
            </TableHeader>
            <TableBody>
              {table.getRowModel().rows.map((row) => (
                <TableRow key={row.id} data-state={row.getIsSelected() ? "selected" : undefined}>
                  {row.getVisibleCells().map((cell) => (
                    <TableCell key={cell.id}>
                      {flexRender(cell.column.columnDef.cell, cell.getContext())}
                    </TableCell>
                  ))}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      {props.pagination ? (
        <div className="flex items-center justify-end gap-ds-sm">
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={props.onPagePrev}
            disabled={!props.hasPrevPage}
          >
            <ChevronLeft aria-hidden="true" className="me-ds-xs size-4 rtl:rotate-180" />
            Previous
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={props.onPageNext}
            disabled={!props.pagination.hasMore}
          >
            Next
            <ChevronRight aria-hidden="true" className="ms-ds-xs size-4 rtl:rotate-180" />
          </Button>
        </div>
      ) : null}
    </div>
  );
}
