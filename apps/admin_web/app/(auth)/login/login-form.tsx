/**
 * T049: LoginForm (Client Component) — uses FormBuilder + zod schema,
 * calls /api/auth/login.
 *
 * Response shapes (per /api/auth/login route handler):
 *   { kind: "ok" }              → redirect to continueTo or "/"
 *   { kind: "mfa_required", partialAuthToken } → push token to sessionStorage, navigate to /mfa
 *   { kind: "error", reasonCode } → render localized error
 */
"use client";

import { useTranslations } from "next-intl";
import { useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { z } from "zod";
import { useFormBuilder, FormShell, applyServerErrors } from "@/components/form-builder/form";
import { FormField } from "@/components/form-builder/form-field";
import { Button } from "@/components/ui/button";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { emitTelemetry } from "@/lib/observability/telemetry";

const PARTIAL_AUTH_KEY = "admin.partialAuthToken";

const schema = z.object({
  email: z.string().email().max(254),
  password: z.string().min(1).max(256),
});

type LoginFormValues = z.infer<typeof schema>;

export function LoginForm() {
  const t = useTranslations("auth");
  const router = useRouter();
  const searchParams = useSearchParams();
  const continueTo = searchParams.get("continueTo") ?? "/";
  const [topError, setTopError] = useState<string | null>(null);

  const form = useFormBuilder({
    schema,
    defaultValues: { email: "", password: "" },
    onSubmit: async (values: LoginFormValues) => {
      setTopError(null);
      emitTelemetry({ name: "admin.login.started" });
      try {
        const res = await fetch("/api/auth/login", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(values),
          credentials: "same-origin",
        });
        const body = (await res.json()) as
          | { kind: "ok" }
          | { kind: "mfa_required"; partialAuthToken: string }
          | { kind: "error"; reasonCode: string; errors?: Record<string, string[]> };

        if (body.kind === "ok") {
          emitTelemetry({ name: "admin.login.success" });
          router.replace(continueTo);
          router.refresh();
          return;
        }
        if (body.kind === "mfa_required") {
          emitTelemetry({ name: "admin.mfa.required" });
          if (typeof window !== "undefined") {
            window.sessionStorage.setItem(PARTIAL_AUTH_KEY, body.partialAuthToken);
          }
          const target = `/mfa?continueTo=${encodeURIComponent(continueTo)}`;
          router.push(target);
          return;
        }
        emitTelemetry({ name: "admin.login.failure", properties: { reason_code: body.reasonCode } });
        if (body.errors) {
          applyServerErrors(form, body.errors, { onUnknown: setTopError });
        } else {
          setTopError(t(`errors.${reasonToKey(body.reasonCode)}`));
        }
      } catch {
        emitTelemetry({ name: "admin.login.failure", properties: { reason_code: "network" } });
        setTopError(t("errors.generic"));
      }
    },
  });

  return (
    <FormShell onSubmit={form.submit}>
      <h1 className="text-2xl font-semibold tracking-tight">{t("login.title")}</h1>

      {topError ? (
        <Alert variant="destructive" role="alert">
          <AlertDescription>{topError}</AlertDescription>
        </Alert>
      ) : null}

      <FormField
        control={form.control}
        name="email"
        label={t("login.email_label")}
        type="email"
        autoComplete="email"
        required
      />
      <FormField
        control={form.control}
        name="password"
        label={t("login.password_label")}
        type="password"
        autoComplete="current-password"
        required
      />

      <Button type="submit" disabled={form.isSubmitting} aria-busy={form.isSubmitting} className="w-full">
        {t("login.submit")}
      </Button>
    </FormShell>
  );
}

/** Map known spec-004 reason codes to message keys; default to `generic`. */
function reasonToKey(reasonCode: string): "invalid_credentials" | "rate_limited" | "account_locked" | "generic" {
  if (reasonCode.includes("invalid_credentials") || reasonCode.includes("auth.signin")) return "invalid_credentials";
  if (reasonCode.includes("rate")) return "rate_limited";
  if (reasonCode.includes("lockout") || reasonCode.includes("locked")) return "account_locked";
  return "generic";
}
