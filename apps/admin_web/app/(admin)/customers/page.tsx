import { getTranslations } from "next-intl/server";
import { requirePermission } from "@/lib/auth/guards";
import { hasPermission } from "@/lib/auth/permissions";
import { getSession } from "@/lib/auth/session";
import { customersApi, type CustomerListRow } from "@/lib/api/clients/customers";
import { PageHeader } from "@/components/shell/page-header";
import { ErrorState } from "@/components/shell/error-state";
import { CustomersTable } from "@/components/customers/list/customers-table";

interface SearchParams {
  q?: string;
  cursor?: string;
}

export default async function CustomersListPage({
  searchParams,
}: {
  searchParams: SearchParams;
}) {
  await requirePermission(["customers.read"], "/customers");
  const session = await getSession();
  const t = await getTranslations("customers.list");

  let rows: CustomerListRow[] = [];
  let nextCursor: string | null = null;
  let errorReason: string | undefined;
  try {
    const page = await customersApi.list({
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
        <CustomersTable
          initialData={rows}
          initialPagination={{
            hasMore: Boolean(nextCursor),
            nextCursor,
          }}
          initialQuery={searchParams.q ?? ""}
          canReadPii={hasPermission(session, "customers.pii.read")}
        />
      )}
    </div>
  );
}
