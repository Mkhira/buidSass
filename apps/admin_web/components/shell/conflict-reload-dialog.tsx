/**
 * T040b: ConflictReloadDialog (FR-025).
 *
 * Surfaced on every 412 row-version conflict by 016 / 017 / 018 / 019.
 * Preserves the local edits (the form fields the admin typed) in a side
 * panel so they can copy them across after reload.
 */
"use client";

import { useTranslations } from "next-intl";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import type { ReactNode } from "react";

export interface ConflictReloadDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Human label for the resource ("product", "stock adjustment", etc.). */
  resourceLabel: string;
  /** Localized name → value pairs the admin had typed locally. */
  preservedFields: Array<{ label: string; value: ReactNode }>;
  onReload: () => void;
}

export function ConflictReloadDialog({
  open,
  onOpenChange,
  resourceLabel,
  preservedFields,
  onReload,
}: ConflictReloadDialogProps) {
  const t = useTranslations("shell.conflict");
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>{t("title", { resource: resourceLabel })}</DialogTitle>
          <DialogDescription>{t("body")}</DialogDescription>
        </DialogHeader>

        {preservedFields.length > 0 ? (
          <div aria-label={t("preserved_label")} className="rounded-md border border-border bg-muted/30 p-ds-md">
            <p className="mb-ds-sm text-xs font-medium uppercase tracking-wide text-muted-foreground">
              {t("preserved_label")}
            </p>
            <Separator className="mb-ds-sm" />
            <dl className="space-y-ds-xs text-sm">
              {preservedFields.map((field, i) => (
                <div key={i} className="grid grid-cols-[180px,1fr] gap-ds-sm">
                  <dt className="text-muted-foreground">{field.label}</dt>
                  <dd className="break-words">{field.value}</dd>
                </div>
              ))}
            </dl>
          </div>
        ) : null}

        <DialogFooter>
          <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
            {t("cancel")}
          </Button>
          <Button type="button" onClick={onReload}>
            {t("reload")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
