/**
 * BellMenu — top-bar notification dropdown (FR-026 / FR-028).
 *
 * v1 wires the dropdown to the stub feed (spec 015 Assumption — single
 * seeded "welcome" entry until spec 023 ships its server endpoints).
 * SSE consumption + reconnect machinery lands when 023 ships; the
 * `eventsource-parser` dependency is already pulled in.
 */
"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { Bell } from "lucide-react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Badge } from "@/components/ui/badge";

interface NotificationEntry {
  id: string;
  kindKey: string;
  titleKey: string;
  bodyKey: string;
  deepLink: string;
  occurredAt: string;
  read: boolean;
}

export function BellMenu() {
  const t = useTranslations();
  const [unreadCount, setUnreadCount] = useState(0);
  const [entries, setEntries] = useState<NotificationEntry[]>([]);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const res = await fetch("/api/notifications/unread", { credentials: "same-origin" });
        if (!res.ok) return;
        const body = (await res.json()) as { entries: NotificationEntry[]; unreadCount: number };
        if (cancelled) return;
        setEntries(body.entries);
        setUnreadCount(body.unreadCount);
      } catch {
        // bell quietly stays at 0 — non-blocking
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        aria-label={t("shell.topbar.notifications")}
        className="relative inline-flex size-8 items-center justify-center rounded-md hover:bg-muted"
      >
        <Bell aria-hidden="true" className="size-5" />
        {unreadCount > 0 ? (
          <Badge
            variant="destructive"
            className="absolute -end-1 -top-1 h-4 min-w-4 rounded-full px-1 text-[10px]"
            aria-label={`${unreadCount} unread`}
          >
            {unreadCount > 99 ? "99+" : unreadCount}
          </Badge>
        ) : null}
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-80">
        <DropdownMenuLabel>{t("shell.topbar.notifications")}</DropdownMenuLabel>
        <DropdownMenuSeparator />
        {entries.length === 0 ? (
          <div className="p-ds-md text-sm text-muted-foreground">
            {t("shell.empty")}
          </div>
        ) : (
          entries.map((entry) => (
            <DropdownMenuItem
              key={entry.id}
              onSelect={() => {
                window.location.href = entry.deepLink;
              }}
              className="flex flex-col items-start gap-ds-xs"
            >
              <span className="text-sm font-medium">{t(entry.titleKey)}</span>
              <span className="text-xs text-muted-foreground">
                {new Date(entry.occurredAt).toLocaleString()}
              </span>
            </DropdownMenuItem>
          ))
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
