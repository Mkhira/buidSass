/**
 * T028: Thin wrapper over spec 004 identity endpoints.
 *
 * Generated types land in `lib/api/types/identity.ts` after `pnpm gen:api`
 * runs against `services/backend_api/openapi.identity.json`. Until the
 * backend OpenAPI doc is on `main`, the shapes here are hand-typed
 * placeholders matching the data-model.md contract.
 */
import { proxyFetch } from "@/lib/api/proxy";
import type { AdminSessionPayload } from "@/lib/auth/session";

export interface SignInRequest {
  email: string;
  password: string;
}

export type SignInResult =
  | { kind: "ok"; session: AdminSessionPayload }
  | { kind: "mfa_required"; partialAuthToken: string; channel?: "totp" | "push" }
  | { kind: "error"; reasonCode: string };

export interface MfaRequest {
  partialAuthToken: string;
  code: string;
}

export interface NavManifest {
  groups: Array<{
    id: string;
    labelKey: string;
    iconKey?: string;
    order: number;
    entries: Array<{
      id: string;
      labelKey: string;
      iconKey?: string;
      route: string;
      requiredPermissions: string[];
      order: number;
      badgeCountKey?: string | null;
    }>;
  }>;
}

export const identityApi = {
  signIn: (req: SignInRequest) =>
    proxyFetch<SignInResult>("/v1/admin/identity/sign-in", {
      method: "POST",
      unauthenticated: true,
      body: JSON.stringify(req),
    }),

  mfa: (req: MfaRequest) =>
    proxyFetch<SignInResult>("/v1/admin/identity/mfa", {
      method: "POST",
      unauthenticated: true,
      body: JSON.stringify(req),
    }),

  refresh: (refreshToken: string) =>
    proxyFetch<AdminSessionPayload>("/v1/admin/identity/refresh", {
      method: "POST",
      unauthenticated: true,
      body: JSON.stringify({ refreshToken }),
    }),

  revoke: (sessionId: string) =>
    proxyFetch<void>("/v1/admin/identity/sessions/revoke", {
      method: "POST",
      body: JSON.stringify({ sessionId }),
    }),

  me: () => proxyFetch<AdminSessionPayload>("/v1/admin/identity/me"),

  navManifest: () => proxyFetch<NavManifest>("/v1/admin/nav-manifest"),

  permissionCatalog: () => proxyFetch<{ keys: string[] }>("/v1/admin/permission-catalog"),

  userPreferences: {
    get: <T>(key: string) =>
      proxyFetch<{ key: string; value: T | null }>(`/v1/admin/me/preferences/${encodeURIComponent(key)}`),
    put: <T>(key: string, value: T) =>
      proxyFetch<void>(`/v1/admin/me/preferences/${encodeURIComponent(key)}`, {
        method: "PUT",
        body: JSON.stringify({ value }),
      }),
  },

  stepUp: {
    start: () => proxyFetch<{ challengeId: string }>("/v1/admin/identity/step-up/start", { method: "POST" }),
    complete: (challengeId: string, code: string) =>
      proxyFetch<{ assertionId: string; expiresAt: number }>(
        "/v1/admin/identity/step-up/complete",
        {
          method: "POST",
          body: JSON.stringify({ challengeId, code }),
        },
      ),
  },
};
