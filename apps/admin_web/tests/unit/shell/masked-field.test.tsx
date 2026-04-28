/**
 * Verifies the contract for `<MaskedField>` — promoted to spec 015's
 * shell, consumed by 018 + 019 (and any future module).
 *
 * Asserts:
 *  - canRead=true → renders the raw value
 *  - canRead=false → renders the localized mask glyph + sr-only label
 *  - emits `customers.pii.field.rendered` exactly once per mode change
 *    (spec 019 FR-007a — debounced regression-signal source)
 */
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import en from "@/messages/en.json";
import { MaskedField } from "@/components/shell/masked-field";
import * as telemetry from "@/lib/observability/telemetry";

function renderWithIntl(ui: React.ReactElement) {
  return render(
    <NextIntlClientProvider locale="en" messages={en} timeZone="Asia/Riyadh">
      {ui}
    </NextIntlClientProvider>,
  );
}

describe("<MaskedField>", () => {
  let emitSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    emitSpy = vi.spyOn(telemetry, "emitTelemetry");
  });
  afterEach(() => {
    cleanup();
    emitSpy.mockRestore();
  });

  it("renders the raw value when canRead is true", () => {
    renderWithIntl(<MaskedField kind="email" value="user@example.com" canRead={true} />);
    expect(screen.getByText("user@example.com")).toBeInTheDocument();
  });

  it("renders the mask glyph + screen-reader label when canRead is false", () => {
    renderWithIntl(<MaskedField kind="email" value="user@example.com" canRead={false} />);
    expect(screen.getByText("••• @•••.com")).toBeInTheDocument();
    expect(screen.getByLabelText("Email hidden")).toBeInTheDocument();
  });

  it("renders the phone mask glyph for kind=phone canRead=false", () => {
    renderWithIntl(<MaskedField kind="phone" value="+966555000112" canRead={false} />);
    expect(screen.getByText("+••• ••• ••• ••12")).toBeInTheDocument();
  });

  it("emits telemetry with mode=masked when canRead is false", () => {
    renderWithIntl(<MaskedField kind="email" value="user@example.com" canRead={false} />);
    expect(emitSpy).toHaveBeenCalledWith({
      name: "customers.pii.field.rendered",
      properties: { mode: "masked", kind: "email" },
    });
  });

  it("emits telemetry with mode=unmasked when canRead is true", () => {
    renderWithIntl(<MaskedField kind="phone" value="+966555000112" canRead={true} />);
    expect(emitSpy).toHaveBeenCalledWith({
      name: "customers.pii.field.rendered",
      properties: { mode: "unmasked", kind: "phone" },
    });
  });
});
