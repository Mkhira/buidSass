/**
 * T028 — RestrictedFlagSection (R9).
 * Toggle + AR + EN rationale. Validation is enforced by the form schema
 * (rationale required when restricted=true). The component itself is a
 * pure controlled UI bundle.
 */
"use client";

import { useTranslations } from "next-intl";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";

export interface RestrictedFlagSectionProps {
  restricted: boolean;
  onRestrictedChange: (next: boolean) => void;
  rationaleEn: string;
  onRationaleEnChange: (next: string) => void;
  rationaleAr: string;
  onRationaleArChange: (next: string) => void;
  errorEn?: string;
  errorAr?: string;
}

export function RestrictedFlagSection(props: RestrictedFlagSectionProps) {
  const t = useTranslations("catalog.product.form");
  return (
    <div className="space-y-ds-sm rounded-md border border-border p-ds-md">
      <div className="flex items-start gap-ds-sm">
        <Checkbox
          id="restricted-flag"
          checked={props.restricted}
          onCheckedChange={(c) => props.onRestrictedChange(c === true)}
        />
        <div>
          <Label htmlFor="restricted-flag">{t("restricted_label")}</Label>
          <p className="text-xs text-muted-foreground">{t("restricted_help")}</p>
        </div>
      </div>
      {props.restricted ? (
        <div className="space-y-ds-sm">
          <div>
            <Label htmlFor="rationale-en">{t("restricted_rationale_en")}</Label>
            <Textarea
              id="rationale-en"
              value={props.rationaleEn}
              onChange={(e) => props.onRationaleEnChange(e.target.value)}
              aria-invalid={Boolean(props.errorEn)}
              required
            />
            {props.errorEn ? (
              <p className="text-xs text-destructive">{props.errorEn}</p>
            ) : null}
          </div>
          <div dir="rtl">
            <Label htmlFor="rationale-ar">{t("restricted_rationale_ar")}</Label>
            <Textarea
              id="rationale-ar"
              value={props.rationaleAr}
              onChange={(e) => props.onRationaleArChange(e.target.value)}
              aria-invalid={Boolean(props.errorAr)}
              required
            />
            {props.errorAr ? (
              <p className="text-xs text-destructive">{props.errorAr}</p>
            ) : null}
          </div>
        </div>
      ) : null}
    </div>
  );
}
