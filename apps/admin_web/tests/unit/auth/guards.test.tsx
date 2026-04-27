/**
 * Verifies the `requireSession()` / `requirePermission()` helpers throw
 * via `redirect()` correctly. Mocks `getSession()` to control the
 * outcome.
 */
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

// `vi.mock` factories are hoisted — keep them self-contained.
vi.mock("next/navigation", () => ({
  redirect: vi.fn((target: string) => {
    const err = new Error(`NEXT_REDIRECT:${target}`);
    (err as unknown as { digest: string }).digest = `NEXT_REDIRECT;replace;${target};307;`;
    throw err;
  }),
}));

vi.mock("@/lib/auth/session", () => ({
  getSession: vi.fn(),
}));

import { redirect } from "next/navigation";
import { getSession } from "@/lib/auth/session";

const redirectMock = vi.mocked(redirect);
const getSessionMock = vi.mocked(getSession);

import { requireSession, requirePermission, requireAnyPermission } from "@/lib/auth/guards";

const SESSION = {
  adminId: "admin_001",
  email: "admin@example.com",
  displayName: "Test Admin",
  roleScope: "ksa" as const,
  roles: ["admin.super"],
  permissions: ["audit.read", "orders.read"],
  accessToken: "tok",
  refreshToken: "ref",
  expiresAt: Date.now() / 1000 + 3600,
  mfaEnrolled: true,
};

describe("auth guards", () => {
  beforeEach(() => {
    redirectMock.mockClear();
    getSessionMock.mockReset();
  });
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("requireSession redirects to /login when no session", async () => {
    getSessionMock.mockResolvedValueOnce(null);
    await expect(requireSession()).rejects.toThrow(/NEXT_REDIRECT:\/login/);
  });

  it("requireSession returns the session when present", async () => {
    getSessionMock.mockResolvedValueOnce(SESSION);
    await expect(requireSession()).resolves.toEqual(SESSION);
  });

  it("requireSession preserves continueTo", async () => {
    getSessionMock.mockResolvedValueOnce(null);
    await expect(requireSession("/audit")).rejects.toThrow(/continueTo=%2Faudit/);
  });

  it("requirePermission redirects to /__forbidden when permission missing", async () => {
    getSessionMock.mockResolvedValueOnce(SESSION);
    await expect(requirePermission("customers.suspend")).rejects.toThrow(/NEXT_REDIRECT:\/__forbidden/);
  });

  it("requirePermission returns the session when permission held", async () => {
    getSessionMock.mockResolvedValueOnce(SESSION);
    await expect(requirePermission("audit.read")).resolves.toEqual(SESSION);
  });

  it("requirePermission with array requires all (AND)", async () => {
    getSessionMock.mockResolvedValueOnce(SESSION);
    await expect(requirePermission(["audit.read", "orders.read"])).resolves.toEqual(SESSION);
    getSessionMock.mockResolvedValueOnce(SESSION);
    await expect(requirePermission(["audit.read", "customers.suspend"])).rejects.toThrow(/__forbidden/);
  });

  it("requireAnyPermission returns held subset on success", async () => {
    getSessionMock.mockResolvedValueOnce(SESSION);
    const result = await requireAnyPermission(["audit.read", "customers.suspend"]);
    expect(result.session).toEqual(SESSION);
    expect(result.held).toEqual(["audit.read"]);
  });

  it("requireAnyPermission redirects when none held", async () => {
    getSessionMock.mockResolvedValueOnce(SESSION);
    await expect(requireAnyPermission(["customers.suspend", "customers.unlock"])).rejects.toThrow(/__forbidden/);
  });
});
