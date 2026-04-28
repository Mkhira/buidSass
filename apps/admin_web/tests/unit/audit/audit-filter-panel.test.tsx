/**
 * T069: AuditFilterPanel — filter changes serialize to URL.
 */
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { NextIntlClientProvider } from "next-intl";
import en from "@/messages/en.json";
import { AuditFilterPanel } from "@/components/audit/audit-filter-panel";

const pushMock = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushMock }),
  useSearchParams: () => new URLSearchParams(""),
}));

function renderPanel(initial: Record<string, string> = {}) {
  return render(
    <NextIntlClientProvider locale="en" messages={en} timeZone="Asia/Riyadh">
      <AuditFilterPanel initial={initial} />
    </NextIntlClientProvider>,
  );
}

describe("<AuditFilterPanel>", () => {
  beforeEach(() => {
    pushMock.mockReset();
  });
  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it("renders all filter fields", () => {
    renderPanel();
    expect(screen.getByLabelText(/actor/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/resource type/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/resource id/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^action$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/market/i)).toBeInTheDocument();
  });

  it("Apply pushes a URL with the typed filters", async () => {
    renderPanel();
    await userEvent.type(screen.getByLabelText(/actor/i), "admin@example.com");
    await userEvent.type(screen.getByLabelText(/^action$/i), "customers.account.suspended");
    fireEvent.click(screen.getByRole("button", { name: /apply/i }));
    await waitFor(() => {
      expect(pushMock).toHaveBeenCalled();
      const target = pushMock.mock.calls[0][0] as string;
      expect(target).toContain("actor=admin");
      expect(target).toContain("actionKey=customers.account.suspended");
    });
  });

  it("Clear pushes /audit (no params)", async () => {
    renderPanel({ actor: "preset" });
    fireEvent.click(screen.getByRole("button", { name: /clear filters/i }));
    await waitFor(() => expect(pushMock).toHaveBeenCalledWith("/audit"));
  });
});
