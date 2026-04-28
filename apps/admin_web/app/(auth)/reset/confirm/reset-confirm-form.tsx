/**
 * T052 (client): Reset confirm form — captures the new password +
 * posts the token from `?token=…` to /api/auth/reset/confirm.
 */
"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { useTranslations } from "next-intl";
import { z } from "zod";
import { useFormBuilder, FormShell } from "@/components/form-builder/form";
import { FormField } from "@/components/form-builder/form-field";
import { Button } from "@/components/ui/button";
import { Alert, AlertDescription } from "@/components/ui/alert";

const schema = z
  .object({
    password: z.string().min(12).max(256),
    confirmPassword: z.string(),
  })
  .refine((d) => d.password === d.confirmPassword, {
    message: "passwords_do_not_match",
    path: ["confirmPassword"],
  });

export function ResetConfirmForm() {
  const t = useTranslations("auth");
  const router = useRouter();
  const searchParams = useSearchParams();
  const token = searchParams.get("token") ?? "";
  const [topError, setTopError] = useState<string | null>(null);

  const form = useFormBuilder({
    schema,
    defaultValues: { password: "", confirmPassword: "" },
    onSubmit: async (values) => {
      setTopError(null);
      try {
        const res = await fetch("/api/auth/reset/confirm", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ token, password: values.password }),
        });
        if (res.ok) {
          router.replace("/login");
          return;
        }
        const body = (await res.json()) as { reasonCode?: string };
        setTopError(t("errors.generic") + (body.reasonCode ? ` (${body.reasonCode})` : ""));
      } catch {
        setTopError(t("errors.generic"));
      }
    },
  });

  if (!token) {
    return (
      <Alert variant="destructive">
        <AlertDescription>{t("errors.generic")}</AlertDescription>
      </Alert>
    );
  }

  return (
    <FormShell onSubmit={form.submit}>
      <h1 className="text-2xl font-semibold tracking-tight">{t("reset.title")}</h1>

      {topError ? (
        <Alert variant="destructive" role="alert">
          <AlertDescription>{topError}</AlertDescription>
        </Alert>
      ) : null}

      <FormField
        control={form.control}
        name="password"
        label={t("login.password_label")}
        type="password"
        autoComplete="new-password"
        required
      />
      <FormField
        control={form.control}
        name="confirmPassword"
        label={t("login.password_label")}
        type="password"
        autoComplete="new-password"
        required
      />

      <Button type="submit" disabled={form.isSubmitting} aria-busy={form.isSubmitting} className="w-full">
        {t("reset.submit")}
      </Button>
    </FormShell>
  );
}
