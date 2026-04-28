/**
 * T057a: PreferencesList — Client Component that reads every saved-view
 * key out of localStorage (transitional storage backend per
 * `<SavedViewsBar>`). Server-backed persistence lands when spec 004
 * ships `/v1/admin/me/preferences` and the storage adapter swaps.
 */
"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { EmptyState } from "@/components/shell/empty-state";
import { Badge } from "@/components/ui/badge";

interface Entry {
  scope: string;
  name: string;
  createdAt: string;
}

const STORAGE_PREFIX = "admin_pref:dataTable:";

export function PreferencesList() {
  const t = useTranslations("saved_views");
  const [entries, setEntries] = useState<Entry[] | null>(null);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const out: Entry[] = [];
    for (let i = 0; i < window.localStorage.length; i++) {
      const key = window.localStorage.key(i);
      if (!key || !key.startsWith(STORAGE_PREFIX)) continue;
      const scope = key.slice(STORAGE_PREFIX.length);
      try {
        const list = JSON.parse(window.localStorage.getItem(key) ?? "[]") as Array<{
          name: string;
          createdAt: string;
        }>;
        for (const v of list) {
          out.push({ scope, name: v.name, createdAt: v.createdAt });
        }
      } catch {
        // skip malformed entries
      }
    }
    setEntries(out);
  }, []);

  if (entries === null) return null;
  if (entries.length === 0) {
    return <EmptyState title={t("empty")} />;
  }

  return (
    <ul className="divide-y divide-border rounded-md border border-border">
      {entries.map((e, i) => (
        <li key={i} className="flex items-center justify-between p-ds-md text-sm">
          <div>
            <p className="font-medium">{e.name}</p>
            <Badge variant="outline" className="mt-ds-xs font-mono text-xs">
              {e.scope}
            </Badge>
          </div>
          <span className="text-xs text-muted-foreground">
            {new Date(e.createdAt).toLocaleDateString()}
          </span>
        </li>
      ))}
    </ul>
  );
}
