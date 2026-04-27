/**
 * T005: spec 005 catalog client.
 *
 * Thin wrapper over the spec 005 backend endpoints. Generated types
 * land under `lib/api/types/catalog.ts` after `pnpm gen:api` runs
 * against `services/backend_api/openapi.catalog.json`. Until that doc
 * is on `main`, the shapes here are hand-typed mirrors of `data-model.md`.
 *
 * NOTE: this module pulls `proxyFetch` (which depends on `next/headers`)
 * and is therefore SERVER-ONLY at runtime. Client Components may still
 * import the exported *types* via `import type { … } from …` — SWC
 * strips those at compile time so they never reach the bundle. Runtime
 * mutation calls from the client must go through the route handlers
 * under `app/api/catalog/...`.
 */
import { proxyFetch } from "@/lib/api/proxy";

// ---- Shared shapes -----------------------------------------------------

export type ProductState = "draft" | "scheduled" | "published";

export interface LocalizedString {
  ar: string;
  en: string;
}

export interface ProductSummary {
  id: string;
  sku: string;
  name: LocalizedString;
  state: ProductState;
  brandId: string | null;
  categoryIds: string[];
  restricted: boolean;
  rowVersion: number;
}

export interface ProductDetail extends ProductSummary {
  description: LocalizedString;
  manufacturerId: string | null;
  attributes: Record<string, unknown>;
  restrictedRationale: LocalizedString | null;
  mediaIds: string[];
  documentIds: string[];
  scheduledPublishAt: string | null;
  pricingRefSummary?: { minorAmount: number; currency: string };
  inventoryRefSummary?: { totalAvailable: number };
}

export interface ProductsPage {
  rows: ProductSummary[];
  nextCursor: string | null;
}

export interface ProductsListFilter {
  state?: ProductState;
  brandId?: string;
  categoryId?: string;
  restricted?: boolean;
  search?: string;
  cursor?: string;
}

export interface CategoryNode {
  id: string;
  parentId: string | null;
  label: LocalizedString;
  order: number;
  active: boolean;
  productCount: number;
  childIds: string[];
}

export interface Brand {
  id: string;
  name: LocalizedString;
  logoMediaId: string | null;
  manufacturerId: string | null;
  active: boolean;
}

export interface Manufacturer {
  id: string;
  name: LocalizedString;
  logoMediaId: string | null;
  active: boolean;
}

export interface BulkImportSession {
  id: string;
  status: "uploaded" | "validating" | "validated" | "committing" | "committed" | "failed";
  uploadedRowCount: number;
  validatedRowCount: number;
  erroredRowCount: number;
  validationReportUrl: string | null;
  submittedBy: string;
  createdAt: string;
}

// ---- Endpoints ---------------------------------------------------------

export const catalogApi = {
  products: {
    list: (filter: ProductsListFilter) => {
      const qs = new URLSearchParams();
      for (const [k, v] of Object.entries(filter)) {
        if (v !== undefined && v !== null && v !== "") qs.set(k, String(v));
      }
      const suffix = qs.toString() ? `?${qs.toString()}` : "";
      return proxyFetch<ProductsPage>(`/v1/admin/catalog/products${suffix}`);
    },
    byId: (productId: string) =>
      proxyFetch<ProductDetail>(`/v1/admin/catalog/products/${encodeURIComponent(productId)}`),
    create: (payload: Partial<ProductDetail>, idempotencyKey?: string) =>
      proxyFetch<ProductDetail>("/v1/admin/catalog/products", {
        method: "POST",
        body: JSON.stringify(payload),
        idempotencyKey,
      }),
    update: (productId: string, payload: Partial<ProductDetail>) =>
      proxyFetch<ProductDetail>(`/v1/admin/catalog/products/${encodeURIComponent(productId)}`, {
        method: "PUT",
        body: JSON.stringify(payload),
      }),
    publish: (productId: string, scheduledAt?: string | null) =>
      proxyFetch<ProductDetail>(
        `/v1/admin/catalog/products/${encodeURIComponent(productId)}/publish`,
        {
          method: "POST",
          // Pass `null` explicitly to unschedule / revert to draft —
          // `undefined` is elided by JSON.stringify and the backend
          // would interpret an empty body as "publish now" instead.
          body: JSON.stringify(scheduledAt === undefined ? {} : { scheduledAt }),
        },
      ),
    discard: (productId: string) =>
      proxyFetch<void>(`/v1/admin/catalog/products/${encodeURIComponent(productId)}/discard`, {
        method: "POST",
      }),
  },
  categories: {
    tree: () => proxyFetch<CategoryNode[]>("/v1/admin/catalog/categories"),
    create: (payload: Partial<CategoryNode>) =>
      proxyFetch<CategoryNode>("/v1/admin/catalog/categories", {
        method: "POST",
        body: JSON.stringify(payload),
      }),
    update: (id: string, payload: Partial<CategoryNode>) =>
      proxyFetch<CategoryNode>(`/v1/admin/catalog/categories/${encodeURIComponent(id)}`, {
        method: "PUT",
        body: JSON.stringify(payload),
      }),
    reorder: (moves: Array<{ id: string; parentId: string | null; order: number }>) =>
      proxyFetch<void>("/v1/admin/catalog/categories/reorder", {
        method: "POST",
        body: JSON.stringify({ moves }),
      }),
    deactivate: (id: string) =>
      proxyFetch<void>(`/v1/admin/catalog/categories/${encodeURIComponent(id)}/deactivate`, {
        method: "POST",
      }),
  },
  brands: {
    list: () => proxyFetch<Brand[]>("/v1/admin/catalog/brands"),
    byId: (id: string) => proxyFetch<Brand>(`/v1/admin/catalog/brands/${encodeURIComponent(id)}`),
    create: (p: Partial<Brand>) =>
      proxyFetch<Brand>("/v1/admin/catalog/brands", { method: "POST", body: JSON.stringify(p) }),
    update: (id: string, p: Partial<Brand>) =>
      proxyFetch<Brand>(`/v1/admin/catalog/brands/${encodeURIComponent(id)}`, {
        method: "PUT",
        body: JSON.stringify(p),
      }),
  },
  manufacturers: {
    list: () => proxyFetch<Manufacturer[]>("/v1/admin/catalog/manufacturers"),
    byId: (id: string) =>
      proxyFetch<Manufacturer>(`/v1/admin/catalog/manufacturers/${encodeURIComponent(id)}`),
    create: (p: Partial<Manufacturer>) =>
      proxyFetch<Manufacturer>("/v1/admin/catalog/manufacturers", {
        method: "POST",
        body: JSON.stringify(p),
      }),
    update: (id: string, p: Partial<Manufacturer>) =>
      proxyFetch<Manufacturer>(`/v1/admin/catalog/manufacturers/${encodeURIComponent(id)}`, {
        method: "PUT",
        body: JSON.stringify(p),
      }),
  },
  bulkImport: {
    upload: (file: FormData) =>
      proxyFetch<BulkImportSession>("/v1/admin/catalog/bulk-import", {
        method: "POST",
        body: file as unknown as BodyInit,
        headers: {}, // multipart — let the browser set Content-Type
      }),
    status: (sessionId: string) =>
      proxyFetch<BulkImportSession>(
        `/v1/admin/catalog/bulk-import/${encodeURIComponent(sessionId)}`,
      ),
    commit: (sessionId: string, expectedRowCount: number) =>
      proxyFetch<BulkImportSession>(
        `/v1/admin/catalog/bulk-import/${encodeURIComponent(sessionId)}/commit`,
        { method: "POST", body: JSON.stringify({ expectedRowCount }) },
      ),
  },
};
