/**
 * T024 — ProductEditorForm.
 *
 * Tabbed AR/EN content using spec 015's `useFormBuilder`. Tracks
 * dirty state via `<DirtyStateGuard>`. On 412 (stale version) surfaces
 * the shared `<ConflictReloadDialog>` — does not reimplement.
 *
 * Per FR-007a, parent page mounts `<AuditForResourceLink>` in the
 * header. Form scope is the editor body only.
 */
"use client";

import { useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { z } from "zod";
import {
  useFormBuilder,
  DirtyStateGuard,
  applyServerErrors,
  FormShell,
} from "@/components/form-builder/form";
import { FormField } from "@/components/form-builder/form-field";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { ConflictReloadDialog } from "@/components/shell/conflict-reload-dialog";
import { catalogApi, type ProductDetail } from "@/lib/api/clients/catalog";
import type { ClientProductState } from "@/lib/catalog/product-state";
import { LocaleTabs } from "./locale-tabs";
import { PublishControls } from "./publish-controls";
import { RestrictedFlagSection } from "./restricted-flag-section";

const productSchema = z
  .object({
    sku: z.string().min(1),
    nameEn: z.string().min(1),
    nameAr: z.string().min(1),
    descriptionEn: z.string().default(""),
    descriptionAr: z.string().default(""),
    brandId: z.string().nullable(),
    manufacturerId: z.string().nullable(),
    restricted: z.boolean(),
    restrictedRationaleEn: z.string().default(""),
    restrictedRationaleAr: z.string().default(""),
    rowVersion: z.number(),
  })
  .superRefine((value, ctx) => {
    if (value.restricted) {
      if (!value.restrictedRationaleEn) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["restrictedRationaleEn"],
          message: "rationale_required",
        });
      }
      if (!value.restrictedRationaleAr) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["restrictedRationaleAr"],
          message: "rationale_required",
        });
      }
    }
  });

type ProductFormValues = z.infer<typeof productSchema>;

export interface ProductEditorFormProps {
  initial: ProductDetail | null;
}

export function ProductEditorForm({ initial }: ProductEditorFormProps) {
  const router = useRouter();
  const t = useTranslations("catalog.product.form");
  const tErrors = useTranslations("catalog.product.errors");
  const tCommon = useTranslations("common");
  const tPublish = useTranslations("catalog.product.publish");
  const [isPending, startTransition] = useTransition();
  const [serverError, setServerError] = useState<string | null>(null);
  const [conflictResource, setConflictResource] = useState<string | null>(null);

  const defaultValues: ProductFormValues = {
    sku: initial?.sku ?? "",
    nameEn: initial?.name.en ?? "",
    nameAr: initial?.name.ar ?? "",
    descriptionEn: initial?.description.en ?? "",
    descriptionAr: initial?.description.ar ?? "",
    brandId: initial?.brandId ?? null,
    manufacturerId: initial?.manufacturerId ?? null,
    restricted: initial?.restricted ?? false,
    restrictedRationaleEn: initial?.restrictedRationale?.en ?? "",
    restrictedRationaleAr: initial?.restrictedRationale?.ar ?? "",
    rowVersion: initial?.rowVersion ?? 0,
  };

  async function persist(values: ProductFormValues): Promise<ProductDetail> {
    const payload: Partial<ProductDetail> = {
      sku: values.sku,
      name: { en: values.nameEn, ar: values.nameAr },
      description: { en: values.descriptionEn, ar: values.descriptionAr },
      brandId: values.brandId,
      manufacturerId: values.manufacturerId,
      restricted: values.restricted,
      restrictedRationale: values.restricted
        ? {
            en: values.restrictedRationaleEn,
            ar: values.restrictedRationaleAr,
          }
        : null,
      rowVersion: values.rowVersion,
    };
    if (initial?.id) {
      return catalogApi.products.update(initial.id, payload);
    }
    return catalogApi.products.create(payload);
  }

  const form = useFormBuilder({
    schema: productSchema,
    defaultValues,
    onSubmit: async (values) => {
      setServerError(null);
      try {
        const saved = await persist(values);
        router.replace(`/catalog/products/${saved.id}`);
      } catch (err) {
        const message = err instanceof Error ? err.message : "unknown";
        if (message.includes("412") || message.toLowerCase().includes("stale")) {
          setConflictResource("Product");
          return;
        }
        setServerError(message);
        if (err && typeof err === "object" && "errors" in err) {
          applyServerErrors(form, (err as { errors: Record<string, string[]> }).errors);
        }
      }
    },
  });

  const restricted = form.watch("restricted");
  const state: ClientProductState = initial?.state ?? "draft";

  function publish(scheduledAt?: string) {
    if (!initial?.id) {
      void form.submit();
      return;
    }
    startTransition(async () => {
      try {
        await catalogApi.products.publish(initial.id, scheduledAt);
        router.refresh();
      } catch (err) {
        setServerError(err instanceof Error ? err.message : "unknown");
      }
    });
  }

  function discard() {
    if (!initial?.id) return;
    startTransition(async () => {
      try {
        await catalogApi.products.discard(initial.id);
        router.replace("/catalog/products");
      } catch (err) {
        setServerError(err instanceof Error ? err.message : "unknown");
      }
    });
  }

  return (
    <FormShell onSubmit={form.submit}>
      <DirtyStateGuard isDirty={form.isDirty} />
      <ConflictReloadDialog
        open={Boolean(conflictResource)}
        onOpenChange={(o) => !o && setConflictResource(null)}
        resourceLabel={conflictResource ?? "product"}
        preservedFields={[
          { label: t("name_en"), value: form.watch("nameEn") ?? "" },
          { label: t("name_ar"), value: form.watch("nameAr") ?? "" },
        ]}
        onReload={() => router.refresh()}
      />
      <FormField
        control={form.control}
        name="sku"
        label={t("sku")}
        autoComplete="off"
        required
      />
      <LocaleTabs
        enLabel={t("name_en")}
        arLabel={t("name_ar")}
        enContent={
          <div className="space-y-ds-sm">
            <FormField
              control={form.control}
              name="nameEn"
              label={t("name_en")}
              required
            />
            <FormField
              control={form.control}
              name="descriptionEn"
              label={t("description_en")}
              render={({ value, onChange, name }) => (
                <Textarea
                  name={name}
                  value={(value as string) ?? ""}
                  onChange={(e) => onChange(e.target.value)}
                  rows={6}
                />
              )}
            />
          </div>
        }
        arContent={
          <div className="space-y-ds-sm">
            <FormField
              control={form.control}
              name="nameAr"
              label={t("name_ar")}
              required
            />
            <FormField
              control={form.control}
              name="descriptionAr"
              label={t("description_ar")}
              render={({ value, onChange, name }) => (
                <Textarea
                  name={name}
                  value={(value as string) ?? ""}
                  onChange={(e) => onChange(e.target.value)}
                  rows={6}
                />
              )}
            />
          </div>
        }
      />
      <RestrictedFlagSection
        restricted={Boolean(restricted)}
        onRestrictedChange={(v) =>
          form.setValue("restricted", v, { shouldDirty: true })
        }
        rationaleEn={form.watch("restrictedRationaleEn") ?? ""}
        onRationaleEnChange={(v) =>
          form.setValue("restrictedRationaleEn", v, { shouldDirty: true })
        }
        rationaleAr={form.watch("restrictedRationaleAr") ?? ""}
        onRationaleArChange={(v) =>
          form.setValue("restrictedRationaleAr", v, { shouldDirty: true })
        }
        errorEn={
          form.formState.errors.restrictedRationaleEn?.message
            ? tErrors("rationale_required")
            : undefined
        }
        errorAr={
          form.formState.errors.restrictedRationaleAr?.message
            ? tErrors("rationale_required")
            : undefined
        }
      />
      {serverError ? (
        <p role="alert" className="text-sm text-destructive">
          {serverError}
        </p>
      ) : null}
      <PublishControls
        state={state}
        isSubmitting={form.isSubmitting || isPending}
        onSaveDraft={() => void form.submit()}
        onPublish={publish}
        onDiscard={discard}
        onTransitionTo={(target) => {
          // From scheduled or published, "draft" means unschedule / revert.
          // Spec 005's publish endpoint accepts `scheduledAt: null` to
          // unschedule; spec may carve a dedicated route later — see
          // docs/admin_web-escalation-log.md for the gap.
          if (!initial?.id) return;
          if (target === "draft") {
            startTransition(async () => {
              try {
                await catalogApi.products.publish(initial.id, undefined);
                router.refresh();
              } catch (err) {
                setServerError(err instanceof Error ? err.message : "unknown");
              }
            });
          }
        }}
      />
      <div className="flex justify-end">
        <Button type="submit" disabled={form.isSubmitting}>
          {form.isSubmitting ? tCommon("save") + "…" : tPublish("save_draft")}
        </Button>
      </div>
    </FormShell>
  );
}
