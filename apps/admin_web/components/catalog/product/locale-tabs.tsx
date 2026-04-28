/**
 * T025 — LocaleTabs.
 * Two-tab control letting the admin switch between EN/AR content panels
 * inside the product editor. Both panels stay mounted so dirty-state
 * tracking holds across tab switches.
 */
"use client";

import { useState, type ReactNode } from "react";
import { cn } from "@/lib/utils";

export interface LocaleTabsProps {
  enLabel: string;
  arLabel: string;
  enContent: ReactNode;
  arContent: ReactNode;
}

export function LocaleTabs({
  enLabel,
  arLabel,
  enContent,
  arContent,
}: LocaleTabsProps) {
  const [active, setActive] = useState<"en" | "ar">("en");
  return (
    <div className="space-y-ds-sm">
      <div role="tablist" className="inline-flex rounded-md border border-border bg-muted/30 p-0.5">
        <button
          role="tab"
          aria-selected={active === "en"}
          onClick={() => setActive("en")}
          className={cn(
            "rounded px-ds-sm py-ds-xs text-sm",
            active === "en" ? "bg-background shadow-sm" : "text-muted-foreground",
          )}
        >
          {enLabel}
        </button>
        <button
          role="tab"
          aria-selected={active === "ar"}
          onClick={() => setActive("ar")}
          className={cn(
            "rounded px-ds-sm py-ds-xs text-sm",
            active === "ar" ? "bg-background shadow-sm" : "text-muted-foreground",
          )}
        >
          {arLabel}
        </button>
      </div>
      <div hidden={active !== "en"}>{enContent}</div>
      <div hidden={active !== "ar"} dir="rtl">
        {arContent}
      </div>
    </div>
  );
}
