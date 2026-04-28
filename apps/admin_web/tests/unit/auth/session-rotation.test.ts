/**
 * T032b verification: dual-secret rotation window.
 *
 * Tests the seal/unseal layer directly (without Next.js cookies()) by
 * round-tripping payloads under a "previous" secret and asserting that:
 *   1. cookies sealed under the previous secret still decrypt
 *   2. the outcome flags `needsReseal: true`
 *   3. cookies sealed under the current secret decrypt without reseal
 *   4. cookies sealed under an unknown secret fail closed (no payload)
 */
import { describe, expect, it, beforeEach, afterEach } from "vitest";
import { sealData } from "iron-session";

const CURRENT = "rotation-test-current-secret-32chars-XX";
const PREVIOUS = "rotation-test-previous-secret-32chars-Y";
const STRANGER = "rotation-test-stranger-secret-32chars-Z";

const PAYLOAD = {
  adminId: "admin_test_001",
  email: "test@example.com",
  displayName: "Test Admin",
  roleScope: "ksa" as const,
  roles: ["admin.super"],
  permissions: ["audit.read"],
  accessToken: "access-1",
  refreshToken: "refresh-1",
  expiresAt: 9999999999,
  mfaEnrolled: true,
};

async function loadSessionModule() {
  // Dynamic import after env vars are set so the module-level secret
  // accessors pick them up.
  return await import("@/lib/auth/session");
}

describe("dual-secret rotation (FR-028d / T032b)", () => {
  let originalCurrent: string | undefined;
  let originalPrev: string | undefined;

  beforeEach(() => {
    originalCurrent = process.env.IRON_SESSION_PASSWORD;
    originalPrev = process.env.IRON_SESSION_PASSWORD_PREV;
  });

  afterEach(() => {
    if (originalCurrent === undefined) delete process.env.IRON_SESSION_PASSWORD;
    else process.env.IRON_SESSION_PASSWORD = originalCurrent;
    if (originalPrev === undefined) delete process.env.IRON_SESSION_PASSWORD_PREV;
    else process.env.IRON_SESSION_PASSWORD_PREV = originalPrev;
  });

  it("decrypts a cookie sealed under the previous secret and flags reseal", async () => {
    process.env.IRON_SESSION_PASSWORD = CURRENT;
    process.env.IRON_SESSION_PASSWORD_PREV = PREVIOUS;
    const sealed = await sealData(PAYLOAD, { password: PREVIOUS });
    const { readSession } = await loadSessionModule();
    const outcome = await readSession(sealed);
    expect(outcome.payload).toBeTruthy();
    expect(outcome.payload?.adminId).toBe(PAYLOAD.adminId);
    expect(outcome.needsReseal).toBe(true);
  });

  it("decrypts a cookie sealed under the current secret without reseal", async () => {
    process.env.IRON_SESSION_PASSWORD = CURRENT;
    process.env.IRON_SESSION_PASSWORD_PREV = PREVIOUS;
    const sealed = await sealData(PAYLOAD, { password: CURRENT });
    const { readSession } = await loadSessionModule();
    const outcome = await readSession(sealed);
    expect(outcome.payload?.adminId).toBe(PAYLOAD.adminId);
    expect(outcome.needsReseal).toBe(false);
  });

  it("fails closed for a cookie sealed under an unknown secret", async () => {
    process.env.IRON_SESSION_PASSWORD = CURRENT;
    process.env.IRON_SESSION_PASSWORD_PREV = PREVIOUS;
    const sealed = await sealData(PAYLOAD, { password: STRANGER });
    const { readSession } = await loadSessionModule();
    const outcome = await readSession(sealed);
    expect(outcome.payload).toBeNull();
    expect(outcome.needsReseal).toBe(false);
  });

  it("returns null for an empty sealed value", async () => {
    process.env.IRON_SESSION_PASSWORD = CURRENT;
    const { readSession } = await loadSessionModule();
    const outcome = await readSession("");
    expect(outcome.payload).toBeNull();
    expect(outcome.needsReseal).toBe(false);
  });
});
