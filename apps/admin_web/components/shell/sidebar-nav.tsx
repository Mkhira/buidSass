/**
 * T036: Sidebar nav — Client Component because it owns active-route state.
 *
 * Reads the manifest from `/api/nav-manifest` (cached `private, max-age=60`).
 * Per FR-009: entries the actor lacks permission for are not rendered
 * (server filters; client doesn't double-check). Permission revocation
 * mid-session triggers a 403 from a feature-page API call → the route's
 * Server-Component layout `redirect`s to `/__forbidden`, and the next nav
 * fetch refreshes the sidebar.
 */
"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils";
import { Skeleton } from "@/components/ui/skeleton";
import * as Icons from "lucide-react";
import type { LucideIcon } from "lucide-react";

interface NavEntry {
  id: string;
  labelKey: string;
  iconKey?: string;
  route: string;
  badgeCountKey?: string | null;
}

interface NavGroup {
  groupId: string;
  labelKey: string;
  entries: NavEntry[];
}

function resolveIcon(key: string | undefined): LucideIcon | null {
  if (!key) return null;
  const pascal = key
    .split(/[-_]/)
    .filter(Boolean)
    .map((p) => p[0].toUpperCase() + p.slice(1))
    .join("");
  const candidate = (Icons as unknown as Record<string, LucideIcon | undefined>)[pascal];
  return candidate ?? null;
}

export function SidebarNav() {
  const t = useTranslations();
  const pathname = usePathname();
  const [groups, setGroups] = useState<NavGroup[] | null>(null);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const res = await fetch("/api/nav-manifest", { credentials: "same-origin" });
        if (!res.ok) {
          if (!cancelled) setGroups([]);
          return;
        }
        const body = (await res.json()) as { groups: NavGroup[] };
        if (!cancelled) setGroups(body.groups);
      } catch {
        if (!cancelled) setGroups([]);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  if (groups === null) {
    return (
      <nav aria-label="Sidebar" className="flex w-60 flex-col gap-ds-md p-ds-md">
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} className="h-8 w-full" />
        ))}
      </nav>
    );
  }

  return (
    <nav
      aria-label="Sidebar"
      className="flex w-60 flex-col gap-ds-md border-e border-border bg-muted/30 p-ds-md"
    >
      {groups.map((group) => (
        <div key={group.groupId}>
          <p className="mb-ds-xs px-ds-xs text-xs font-medium uppercase tracking-wide text-muted-foreground">
            {t(group.labelKey)}
          </p>
          <ul className="space-y-ds-xs">
            {group.entries.map((entry) => {
              const Icon = resolveIcon(entry.iconKey);
              const active = pathname === entry.route || pathname.startsWith(`${entry.route}/`);
              return (
                <li key={entry.id}>
                  <Link
                    href={entry.route}
                    className={cn(
                      "flex items-center gap-ds-sm rounded-md px-ds-sm py-ds-xs text-sm transition-colors",
                      active
                        ? "bg-primary/10 font-medium text-primary"
                        : "text-foreground hover:bg-muted",
                    )}
                    aria-current={active ? "page" : undefined}
                  >
                    {Icon ? <Icon aria-hidden="true" className="size-4" /> : null}
                    <span>{t(entry.labelKey)}</span>
                  </Link>
                </li>
              );
            })}
          </ul>
        </div>
      ))}
    </nav>
  );
}
