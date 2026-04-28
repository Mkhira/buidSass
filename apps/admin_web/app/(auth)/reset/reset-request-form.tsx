/**
 * T052 (client): Reset request form — captures the email + posts to
 * /api/auth/reset/request. Always shows the same "we sent a link if it
 * exists" outcome to avoid email enumeration.
 */
"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { z } from "zod";
import { useFormBuilder, FormShell } from "@/components/form-builder/form";
import { FormField } from "@/components/form-builder/form-field";
import { Button } from "@/components/ui/button";
import { Alert, AlertDescription } from "@/components/ui/alert";

const schema = z.object({ email: z.string().email().max(254) });

export function ResetRequestForm() {
  const t = useTranslations("auth");
  const [submitted, setSubmitted] = useState(false);

  const form = useFormBuilder({
    schema,
    defaultValues: { email: "" },
    onSubmit: async (values) => {
      try {
        await fetch("/api/auth/reset/request", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(values),
        });
      } finally {
        setSubmitted(true);
      }
    },
  });

  if (submitted) {
    return (
      <Alert>
        <AlertDescription>{t("login.forgot_password")}</AlertDescription>
      </Alert>
    );
  }

  return (
    <FormShell onSubmit={form.submit}>
      <h1 className="text-2xl font-semibold tracking-tight">{t("reset.title")}</h1>
      <FormField
        control={form.control}
        name="email"
        label={t("login.email_label")}
        type="email"
        autoComplete="email"
        required
      />
      <Button type="submit" disabled={form.isSubmitting} aria-busy={form.isSubmitting} className="w-full">
        {t("reset.submit")}
      </Button>
    </FormShell>
  );
}
