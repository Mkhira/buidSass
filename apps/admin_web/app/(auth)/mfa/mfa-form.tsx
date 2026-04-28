/**
 * T051: MFA form — TOTP entry. Reads the partial-auth token from
 * sessionStorage (set by LoginForm), POSTs to /api/auth/mfa.
 */
"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { useRouter, useSearchParams } from "next/navigation";
import { z } from "zod";
import { useFormBuilder, FormShell } from "@/components/form-builder/form";
import { FormField } from "@/components/form-builder/form-field";
import { Button } from "@/components/ui/button";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { emitTelemetry } from "@/lib/observability/telemetry";

const PARTIAL_AUTH_KEY = "admin.partialAuthToken";

const schema = z.object({
  code: z.string().regex(/^\d{6}$/, "6 digits"),
});

type MfaFormValues = z.infer<typeof schema>;

export function MfaForm() {
  const t = useTranslations("auth");
  const router = useRouter();
  const searchParams = useSearchParams();
  // Validate continueTo is a same-origin absolute path. Reject
  // protocol-relative `//` and any URL with a scheme (e.g.,
  // `javascript:`) per Next.js docs — router.replace executes
  // `javascript:` URLs in the page context (XSS) and cross-origin
  // values open the user up to redirect attacks.
  const rawContinueTo = searchParams.get("continueTo");
  const continueTo =
    rawContinueTo && rawContinueTo.startsWith("/") && !rawContinueTo.startsWith("//")
      ? rawContinueTo
      : "/";
  const [topError, setTopError] = useState<string | null>(null);
  const [partialAuthToken, setPartialAuthToken] = useState<string | null>(null);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const tok = window.sessionStorage.getItem(PARTIAL_AUTH_KEY);
    if (!tok) {
      // No token — middleware would route here on direct nav. Send
      // them back to /login to start over.
      router.replace("/login");
      return;
    }
    setPartialAuthToken(tok);
  }, [router]);

  const form = useFormBuilder({
    schema,
    defaultValues: { code: "" },
    onSubmit: async (values: MfaFormValues) => {
      if (!partialAuthToken) return;
      setTopError(null);
      try {
        const res = await fetch("/api/auth/mfa", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ partialAuthToken, code: values.code }),
          credentials: "same-origin",
        });
        const body = (await res.json()) as
          | { kind: "ok" }
          | { kind: "error"; reasonCode: string };

        if (body.kind === "ok") {
          emitTelemetry({ name: "admin.mfa.success" });
          window.sessionStorage.removeItem(PARTIAL_AUTH_KEY);
          router.replace(continueTo);
          router.refresh();
          return;
        }
        emitTelemetry({ name: "admin.mfa.failure", properties: { reason_code: body.reasonCode } });
        setTopError(t(`errors.${reasonToKey(body.reasonCode)}`));
      } catch {
        emitTelemetry({ name: "admin.mfa.failure", properties: { reason_code: "network" } });
        setTopError(t("errors.generic"));
      }
    },
  });

  if (!partialAuthToken) {
    return null;
  }

  return (
    <FormShell onSubmit={form.submit}>
      <h1 className="text-2xl font-semibold tracking-tight">{t("mfa.title")}</h1>

      {topError ? (
        <Alert variant="destructive" role="alert">
          <AlertDescription>{topError}</AlertDescription>
        </Alert>
      ) : null}

      <FormField
        control={form.control}
        name="code"
        label={t("mfa.title")}
        type="text"
        autoComplete="one-time-code"
        required
      />

      <Button type="submit" disabled={form.isSubmitting} aria-busy={form.isSubmitting} className="w-full">
        {t("mfa.submit")}
      </Button>
    </FormShell>
  );
}

function reasonToKey(reasonCode: string): "mfa_invalid" | "rate_limited" | "generic" {
  if (reasonCode.includes("invalid") || reasonCode.includes("mfa")) return "mfa_invalid";
  if (reasonCode.includes("rate")) return "rate_limited";
  return "generic";
}
