/**
 * T054: LoginForm tests.
 *
 * Covers:
 *  - Renders email + password fields with required indicators
 *  - Successful login → calls router.replace with continueTo
 *  - mfa_required → stashes partial-auth-token + navigates to /mfa
 *  - error response → renders localized error
 */
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { NextIntlClientProvider } from "next-intl";
import en from "@/messages/en.json";
import { LoginForm } from "@/app/(auth)/login/login-form";

const replaceMock = vi.fn();
const pushMock = vi.fn();
const refreshMock = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace: replaceMock, push: pushMock, refresh: refreshMock }),
  useSearchParams: () => new URLSearchParams(""),
}));

function renderForm() {
  return render(
    <NextIntlClientProvider locale="en" messages={en} timeZone="Asia/Riyadh">
      <LoginForm />
    </NextIntlClientProvider>,
  );
}

describe("<LoginForm>", () => {
  beforeEach(() => {
    replaceMock.mockReset();
    pushMock.mockReset();
    refreshMock.mockReset();
    if (typeof window !== "undefined") window.sessionStorage.clear();
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it("renders email + password + submit", () => {
    renderForm();
    expect(screen.getByLabelText(/email/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/password/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /sign in/i })).toBeInTheDocument();
  });

  it("on { kind: 'ok' } redirects to landing", async () => {
    vi.spyOn(global, "fetch").mockResolvedValueOnce(
      new Response(JSON.stringify({ kind: "ok" }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    );
    renderForm();
    await userEvent.type(screen.getByLabelText(/email/i), "admin@example.com");
    await userEvent.type(screen.getByLabelText(/password/i), "correct-horse-battery");
    fireEvent.click(screen.getByRole("button", { name: /sign in/i }));
    await waitFor(() => expect(replaceMock).toHaveBeenCalledWith("/"));
  });

  it("on { kind: 'mfa_required' } stashes the partial-auth token and pushes to /mfa", async () => {
    vi.spyOn(global, "fetch").mockResolvedValueOnce(
      new Response(JSON.stringify({ kind: "mfa_required", partialAuthToken: "tok-abc" }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    );
    renderForm();
    await userEvent.type(screen.getByLabelText(/email/i), "admin@example.com");
    await userEvent.type(screen.getByLabelText(/password/i), "correct-horse-battery");
    fireEvent.click(screen.getByRole("button", { name: /sign in/i }));
    await waitFor(() => {
      expect(window.sessionStorage.getItem("admin.partialAuthToken")).toBe("tok-abc");
      expect(pushMock).toHaveBeenCalledWith(expect.stringMatching(/^\/mfa/));
    });
  });

  it("on error response renders the localized error", async () => {
    vi.spyOn(global, "fetch").mockResolvedValueOnce(
      new Response(JSON.stringify({ kind: "error", reasonCode: "invalid_credentials" }), {
        status: 401,
        headers: { "Content-Type": "application/json" },
      }),
    );
    renderForm();
    await userEvent.type(screen.getByLabelText(/email/i), "admin@example.com");
    await userEvent.type(screen.getByLabelText(/password/i), "wrong-password");
    fireEvent.click(screen.getByRole("button", { name: /sign in/i }));
    await waitFor(() => {
      expect(screen.getByRole("alert")).toHaveTextContent(/incorrect/i);
    });
  });
});
