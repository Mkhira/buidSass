/**
 * T040f: MaskedField (FR-022a + FR-025 + spec 019 FR-007a).
 *
 * Single-source PII redaction component used by every admin spec.
 * - When `canRead` is true → renders the raw value.
 * - When `canRead` is false → renders the localized mask glyph
 *   (`••• @•••.com`, `+••• ••• ••• ••12`, generic `•••`); screen readers
 *   announce the localized "email hidden" / "phone hidden" string,
 *   never the glyph.
 *
 * Emits the `customers.pii.field.rendered` telemetry event per spec 019
 * FR-007a — debounced to one emission per mount + `mode` change.
 */
"use client";

import { useEffect, useRef } from "react";
import { useTranslations } from "next-intl";
import { emitTelemetry } from "@/lib/observability/telemetry";

export type MaskedFieldKind = "email" | "phone" | "generic";

export interface MaskedFieldProps {
  kind: MaskedFieldKind;
  value: string | null | undefined;
  canRead: boolean;
  /** Optional className for the wrapping span. */
  className?: string;
}

const MASK_GLYPHS: Record<MaskedFieldKind, string> = {
  email: "••• @•••.com",
  phone: "+••• ••• ••• ••12",
  generic: "•••",
};

const SR_KEYS: Record<MaskedFieldKind, string> = {
  email: "email_hidden",
  phone: "phone_hidden",
  generic: "value_hidden",
};

export function MaskedField({ kind, value, canRead, className }: MaskedFieldProps) {
  const t = useTranslations("shell.masked_field");
  const mode: "masked" | "unmasked" = canRead && value ? "unmasked" : "masked";
  const lastEmittedRef = useRef<{ mode: typeof mode; kind: MaskedFieldKind } | null>(null);

  useEffect(() => {
    const last = lastEmittedRef.current;
    if (last && last.mode === mode && last.kind === kind) return;
    emitTelemetry({
      name: "customers.pii.field.rendered",
      properties: { mode, kind },
    });
    lastEmittedRef.current = { mode, kind };
  }, [mode, kind]);

  if (mode === "unmasked") {
    return <span className={className}>{value}</span>;
  }

  const srLabel = t(SR_KEYS[kind]);
  return (
    <span className={className} aria-label={srLabel} title={srLabel}>
      <span aria-hidden="true">{MASK_GLYPHS[kind]}</span>
      <span className="sr-only">{srLabel}</span>
    </span>
  );
}
