/**
 * spec 004 (admin-customers) client.
 *
 * Hand-typed mirrors of `data-model.md` until `pnpm gen:api` against
 * `services/backend_api/openapi.identity.json` lands generated types.
 */
import { proxyFetch } from "@/lib/api/proxy";

export interface CustomerListRow {
  id: string;
  displayName: string;
  emailMasked: string;
  phoneMasked: string;
  marketCode: "ksa" | "eg";
  b2bFlag: boolean;
  verificationState: string;
  accountState: "active" | "suspended" | "closed";
  lastActiveAt: string;
  createdAt: string;
}

export interface CustomerListPage {
  rows: CustomerListRow[];
  nextCursor: string | null;
}

export interface CustomersListFilters {
  searchQuery?: string | null;
  marketCode?: "ksa" | "eg" | null;
  b2bFlag?: boolean | null;
  accountState?: "active" | "suspended" | "closed" | null;
  cursor?: string;
}

export interface Address {
  id: string;
  label: string;
  recipient: string;
  line1: string;
  line2?: string | null;
  city: string;
  region?: string | null;
  country: string;
  postalCode: string;
  phone: string;
  marketCode: "ksa" | "eg";
  isDefault: boolean;
}

export interface CompanyLinkage {
  kind: "company_owner" | "company_member";
  parentCompany: { id: string; name: string; active: boolean };
  branches:
    | Array<{ id: string; name: string; active: boolean }>
    | null;
  members:
    | Array<{ id: string; displayName: string; role: string; active: boolean }>
    | null;
  approverPreview: {
    requiresApproval: boolean;
    threshold?: number;
  } | null;
}

export interface CustomerProfile extends CustomerListRow {
  email: string | null;
  phone: string | null;
  locale: "ar" | "en";
  roles: Array<{
    key: string;
    labelKey: string;
    scope: "customer" | "company";
  }>;
  addressesPreview: Address[];
  addressesCount: number;
  ordersSummary: {
    count: number;
    mostRecentOrderId?: string;
    mostRecentOrderNumber?: string;
  };
  companyLinkage: CompanyLinkage | null;
  rowVersion: number;
}

export interface AccountActionPayload {
  reasonNote: string;
  rowVersion: number;
}

function toQs(filter: Record<string, unknown>): string {
  const qs = new URLSearchParams();
  for (const [k, v] of Object.entries(filter)) {
    if (v === undefined || v === null || v === "") continue;
    qs.set(k, String(v));
  }
  return qs.toString() ? `?${qs.toString()}` : "";
}

export const customersApi = {
  list: (filter: CustomersListFilters) =>
    proxyFetch<CustomerListPage>(
      `/v1/admin/customers${toQs(filter as unknown as Record<string, unknown>)}`,
    ),
  detail: (customerId: string) =>
    proxyFetch<CustomerProfile>(
      `/v1/admin/customers/${encodeURIComponent(customerId)}`,
    ),
  suspend: (
    customerId: string,
    payload: AccountActionPayload,
    idempotencyKey: string,
    stepUpAssertion: string,
  ) =>
    proxyFetch<CustomerProfile>(
      `/v1/admin/customers/${encodeURIComponent(customerId)}/suspend`,
      {
        method: "POST",
        body: JSON.stringify(payload),
        idempotencyKey,
        headers: { "X-StepUp-Assertion": stepUpAssertion },
      },
    ),
  unlock: (
    customerId: string,
    payload: AccountActionPayload,
    idempotencyKey: string,
    stepUpAssertion: string,
  ) =>
    proxyFetch<CustomerProfile>(
      `/v1/admin/customers/${encodeURIComponent(customerId)}/unlock`,
      {
        method: "POST",
        body: JSON.stringify(payload),
        idempotencyKey,
        headers: { "X-StepUp-Assertion": stepUpAssertion },
      },
    ),
  triggerPasswordReset: (
    customerId: string,
    payload: AccountActionPayload,
    idempotencyKey: string,
    stepUpAssertion: string,
  ) =>
    proxyFetch<CustomerProfile>(
      `/v1/admin/customers/${encodeURIComponent(customerId)}/password-reset`,
      {
        method: "POST",
        body: JSON.stringify(payload),
        idempotencyKey,
        headers: { "X-StepUp-Assertion": stepUpAssertion },
      },
    ),
};
