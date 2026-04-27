/**
 * T021 — Adjust form.
 *
 * - FR-004: enforces mandatory note (≥ 10 chars) for theft_loss /
 *   write_off_below_zero / breakage.
 * - FR-005: blocks below-zero adjustments unless the admin holds
 *   `inventory.writeoff_below_zero`.
 * - 412 conflict reuses spec 015's `<ConflictReloadDialog>`.
 */
"use client";

import { useState, useTransition, useMemo } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { z } from "zod";
import {
  useFormBuilder,
  DirtyStateGuard,
  FormShell,
} from "@/components/form-builder/form";
import { FormField } from "@/components/form-builder/form-field";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { ConflictReloadDialog } from "@/components/shell/conflict-reload-dialog";
import { inventoryApi, type ReasonCode } from "@/lib/api/clients/inventory";
import {
  validateAdjustment,
  type AdjustmentSubmissionState,
} from "@/lib/inventory/adjust-state";
import { requiresNote as reasonRequiresNote } from "@/lib/inventory/reason-codes";

const adjustSchema = z.object({
  warehouseId: z.string().min(1),
  skuId: z.string().min(1),
  delta: z
    .number()
    .int()
    .refine((v) => v !== 0, { message: "delta_zero" }),
  reasonCode: z.string().min(1),
  batchId: z.string().nullable().optional(),
  note: z.string().max(2000).default(""),
  rowVersion: z.number(),
});

type AdjustFormValues = z.infer<typeof adjustSchema>;

export interface AdjustFormProps {
  initialWarehouseId?: string;
  initialSkuId?: string;
  reasonCodes: ReasonCode[];
  /** Current snapshot for FR-005 below-zero pre-check. */
  currentOnHand: number;
  rowVersion: number;
  /** Whether the admin holds `inventory.writeoff_below_zero`. */
  hasWriteoffBelowZeroPermission: boolean;
}

export function AdjustForm({
  initialWarehouseId,
  initialSkuId,
  reasonCodes,
  currentOnHand,
  rowVersion,
  hasWriteoffBelowZeroPermission,
}: AdjustFormProps) {
  const router = useRouter();
  const t = useTranslations("inventory.adjust");
  const tReasons = useTranslations("inventory.reason_codes");
  const [isPending, startTransition] = useTransition();
  const [submission, setSubmission] = useState<AdjustmentSubmissionState>({
    kind: "idle",
  });
  const [conflictOpen, setConflictOpen] = useState(false);

  const idempotencyKey = useMemo(() => crypto.randomUUID(), []);

  const form = useFormBuilder({
    schema: adjustSchema,
    defaultValues: {
      warehouseId: initialWarehouseId ?? "",
      skuId: initialSkuId ?? "",
      delta: 0,
      reasonCode: "",
      batchId: null,
      note: "",
      rowVersion,
    },
    onSubmit: async (values) => {
      const validation = validateAdjustment({
        delta: values.delta,
        reasonCode: values.reasonCode,
        note: values.note,
        onHand: currentOnHand,
        hasWriteoffBelowZeroPermission,
      });
      if (validation.kind !== "validating") {
        setSubmission(validation);
        return;
      }
      setSubmission({ kind: "submitting", idempotencyKey });
      try {
        const result = await inventoryApi.adjustments.create(
          {
            warehouseId: values.warehouseId,
            skuId: values.skuId,
            delta: values.delta,
            reasonCode: values.reasonCode,
            batchId: values.batchId ?? null,
            note: values.note,
            rowVersion: values.rowVersion,
          },
          idempotencyKey,
        );
        setSubmission({
          kind: "submitted",
          ledgerEntryId: result.ledgerEntryId,
        });
        startTransition(() => {
          router.replace(
            `/inventory/stock/${encodeURIComponent(values.skuId)}?warehouse=${encodeURIComponent(values.warehouseId)}`,
          );
        });
      } catch (err) {
        const message = err instanceof Error ? err.message : "unknown";
        if (message.includes("412")) {
          setSubmission({ kind: "conflict_detected", reasonCode: "412" });
          setConflictOpen(true);
        } else if (message.includes("inventory.permission_revoked")) {
          setSubmission({
            kind: "failed_terminal",
            reasonCode: "inventory.permission_revoked",
          });
        } else {
          setSubmission({ kind: "failed", reason: message, idempotencyKey });
        }
      }
    },
  });

  const reasonCode = form.watch("reasonCode");
  const noteRequired = Boolean(reasonCode) && reasonRequiresNote(reasonCode);

  return (
    <FormShell onSubmit={form.submit}>
      <DirtyStateGuard isDirty={form.isDirty} />
      <ConflictReloadDialog
        open={conflictOpen}
        onOpenChange={(o) => setConflictOpen(o)}
        resourceLabel="adjustment"
        preservedFields={[
          { label: t("delta"), value: String(form.watch("delta")) },
          { label: t("reason"), value: reasonCode || "—" },
          { label: t("note"), value: form.watch("note") || "—" },
        ]}
        onReload={() => router.refresh()}
      />
      <FormField
        control={form.control}
        name="warehouseId"
        label={t("warehouse")}
        required
      />
      <FormField control={form.control} name="skuId" label={t("sku")} required />
      <FormField
        control={form.control}
        name="delta"
        label={t("delta")}
        description={t("delta_help")}
        type="number"
        required
      />
      <FormField
        control={form.control}
        name="reasonCode"
        label={t("reason")}
        required
        render={({ value, onChange, name }) => (
          <select
            name={name}
            value={(value as string) ?? ""}
            onChange={(e) => onChange(e.target.value)}
            className="w-full rounded-md border border-border bg-background p-ds-sm"
          >
            <option value="">—</option>
            {reasonCodes.map((rc) => (
              <option key={rc.code} value={rc.code}>
                {tReasons(rc.code as never)}
              </option>
            ))}
          </select>
        )}
      />
      <FormField
        control={form.control}
        name="note"
        label={t("note")}
        description={noteRequired ? t("note_required_help") : undefined}
        render={({ value, onChange, name }) => (
          <Textarea
            name={name}
            value={(value as string) ?? ""}
            onChange={(e) => onChange(e.target.value)}
            rows={4}
            aria-required={noteRequired}
          />
        )}
      />
      {submission.kind === "below_zero_blocked" ? (
        <p role="alert" className="text-sm text-destructive">
          {t("below_zero_blocked")}
        </p>
      ) : null}
      {submission.kind === "missing_note_blocked" ? (
        <p role="alert" className="text-sm text-destructive">
          {t("missing_note_blocked")}
        </p>
      ) : null}
      {submission.kind === "failed_terminal" ? (
        <p role="alert" className="text-sm text-destructive">
          {t("permission_revoked")}
        </p>
      ) : null}
      {submission.kind === "failed" ? (
        <p role="alert" className="text-sm text-destructive">
          {submission.reason}
        </p>
      ) : null}
      <div className="flex justify-end">
        <Button
          type="submit"
          disabled={form.isSubmitting || isPending}
        >
          {t("submit")}
        </Button>
      </div>
    </FormShell>
  );
}
