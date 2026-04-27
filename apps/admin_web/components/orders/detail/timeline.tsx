/**
 * T031 — order timeline (4-stream view).
 *
 * v1 ships a non-virtualized list — virtualization (research §R2)
 * lands when timelines grow beyond a few hundred entries.
 */
"use client";

import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import type { TimelineEntry } from "@/lib/api/clients/orders";
import { cn } from "@/lib/utils";

const STREAM_COLORS: Record<TimelineEntry["machine"], string> = {
  order: "bg-blue-500",
  payment: "bg-purple-500",
  fulfillment: "bg-green-500",
  refund: "bg-amber-500",
};

export function Timeline({ entries }: { entries: TimelineEntry[] }) {
  const fmt = useFormatter();
  const t = useTranslations("orders.detail");
  if (entries.length === 0) {
    return <p className="text-sm text-muted-foreground">{t("timeline")}</p>;
  }
  return (
    <ol className="space-y-ds-sm">
      {entries.map((e) => (
        <li key={e.id} className="flex gap-ds-sm">
          <span
            aria-hidden="true"
            className={cn(
              "mt-1 size-2 shrink-0 rounded-full",
              STREAM_COLORS[e.machine],
            )}
          />
          <div className="flex-1">
            <p className="text-sm">
              <span className="font-medium">{e.machine}</span>{" "}
              <span className="text-muted-foreground">
                {e.fromState} → {e.toState}
              </span>
            </p>
            <p className="text-xs text-muted-foreground">
              {fmt.dateTime(new Date(e.occurredAt), {
                dateStyle: "medium",
                timeStyle: "short",
              })}{" "}
              · {e.actor.displayName ?? e.actor.kind}
              {e.auditPermalink && e.auditPermalink.length > 0 ? (
                <>
                  {" · "}
                  <Link
                    href={e.auditPermalink}
                    className="underline-offset-4 hover:underline"
                  >
                    audit
                  </Link>
                </>
              ) : null}
            </p>
            {e.reasonNote ? (
              <p className="text-sm">{e.reasonNote}</p>
            ) : null}
          </div>
        </li>
      ))}
    </ol>
  );
}
