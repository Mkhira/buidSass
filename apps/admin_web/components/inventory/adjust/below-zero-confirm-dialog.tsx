/**
 * T023 — BelowZeroConfirmDialog (FR-005).
 *
 * Surfaced when the admin holds `inventory.writeoff_below_zero` AND
 * the typed delta would push on-hand below zero. The admin must
 * explicitly acknowledge the write-off before the form submits.
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

export interface BelowZeroConfirmDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** On-hand before the adjustment + the typed delta. */
  available: number;
  delta: number;
  onConfirm: () => void;
}

export function BelowZeroConfirmDialog({
  open,
  onOpenChange,
  available,
  delta,
  onConfirm,
}: BelowZeroConfirmDialogProps) {
  const t = useTranslations("inventory.adjust");
  const tCommon = useTranslations("common");
  const projected = available + delta;
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("below_zero_confirm_title")}</DialogTitle>
          <DialogDescription>{t("below_zero_confirm_body")}</DialogDescription>
        </DialogHeader>
        <p className="text-sm text-muted-foreground">
          {available} → {projected}
        </p>
        <DialogFooter>
          <Button
            type="button"
            variant="ghost"
            onClick={() => onOpenChange(false)}
          >
            {tCommon("cancel")}
          </Button>
          <Button
            type="button"
            variant="destructive"
            onClick={() => {
              onConfirm();
              onOpenChange(false);
            }}
          >
            {tCommon("confirm")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
