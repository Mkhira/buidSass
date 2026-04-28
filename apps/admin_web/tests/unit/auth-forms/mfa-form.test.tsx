/**
 * T054: MfaForm tests.
 *
 * Covers:
 *  - No partial-auth-token in sessionStorage → redirects to /login
 *  - Successful MFA → clears token + redirects to landing
 *  - Invalid code → renders localized error
 */
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { NextIntlClientProvider } from "next-intl";
import en from "@/messages/en.json";
import { MfaForm } from "@/app/(auth)/mfa/mfa-form";

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
      <MfaForm />
    </NextIntlClientProvider>,
  );
}

describe("<MfaForm>", () => {
  beforeEach(() => {
    replaceMock.mockReset();
    pushMock.mockReset();
    refreshMock.mockReset();
    window.sessionStorage.clear();
  });
  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it("redirects to /login when no partial-auth token is present", async () => {
    renderForm();
    // Includes a continueTo param so the user lands where they
    // intended after re-authenticating.
    await waitFor(() =>
      expect(replaceMock).toHaveBeenCalledWith(expect.stringMatching(/^\/login\?continueTo=/)),
    );
  });

  it("on success clears the token + redirects to landing", async () => {
    window.sessionStorage.setItem("admin.partialAuthToken", "tok-abc");
    vi.spyOn(global, "fetch").mockResolvedValueOnce(
      new Response(JSON.stringify({ kind: "ok" }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    );
    renderForm();
    await waitFor(() => expect(screen.queryByLabelText(/authenticator/i)).toBeInTheDocument());
    await userEvent.type(screen.getByLabelText(/authenticator/i), "123456");
    fireEvent.click(screen.getByRole("button", { name: /verify/i }));
    await waitFor(() => {
      expect(replaceMock).toHaveBeenCalledWith("/");
      expect(window.sessionStorage.getItem("admin.partialAuthToken")).toBeNull();
    });
  });

  it("on invalid code renders the localized error", async () => {
    window.sessionStorage.setItem("admin.partialAuthToken", "tok-abc");
    vi.spyOn(global, "fetch").mockResolvedValueOnce(
      new Response(JSON.stringify({ kind: "error", reasonCode: "mfa.invalid" }), {
        status: 401,
        headers: { "Content-Type": "application/json" },
      }),
    );
    renderForm();
    await waitFor(() => expect(screen.queryByLabelText(/authenticator/i)).toBeInTheDocument());
    await userEvent.type(screen.getByLabelText(/authenticator/i), "654321");
    fireEvent.click(screen.getByRole("button", { name: /verify/i }));
    await waitFor(() => {
      expect(screen.getByRole("alert")).toHaveTextContent(/code didn't match/i);
    });
  });
});
