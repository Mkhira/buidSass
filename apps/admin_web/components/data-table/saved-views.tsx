/**
 * T042: SavedViews (FR-023).
 *
 * Per-screen saved views, persisted via spec 004's user-preferences
 * endpoint. Until 004 ships that endpoint, falls back to localStorage
 * keyed `admin_pref:dataTable:<viewKey>` — a transitional fallback per
 * the **Saved views storage** Assumption + spec 015 FR-023's escalation
 * policy. Promotion to server-side persistence is a one-line swap of
 * the storage backend.
 *
 * The component is generic over the filter shape so 016/017/018/019
 * each pass their own `<TFilter>`.
 */
"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { ChevronDown, Save, Trash2 } from "lucide-react";

export interface SavedView<TFilter> {
  id: string;
  name: string;
  filter: TFilter;
  createdAt: string;
}

export interface SavedViewsBarProps<TFilter> {
  /** Stable key per screen, e.g. `audit.list`, `customers.list`. */
  viewKey: string;
  /** The current active filter shape. Saving a view captures this. */
  currentFilter: TFilter;
  /** Called when the user picks a saved view — apply its filter. */
  onApply: (filter: TFilter) => void;
}

const STORAGE_NS = "admin_pref:dataTable:";

interface StorageBackend {
  list(viewKey: string): Promise<SavedView<unknown>[]>;
  put(viewKey: string, view: SavedView<unknown>): Promise<void>;
  remove(viewKey: string, id: string): Promise<void>;
}

const localStorageBackend: StorageBackend = {
  async list(viewKey) {
    if (typeof window === "undefined") return [];
    try {
      const raw = window.localStorage.getItem(STORAGE_NS + viewKey);
      return raw ? (JSON.parse(raw) as SavedView<unknown>[]) : [];
    } catch {
      return [];
    }
  },
  async put(viewKey, view) {
    if (typeof window === "undefined") return;
    const list = await this.list(viewKey);
    const next = [...list.filter((v) => v.id !== view.id), view];
    window.localStorage.setItem(STORAGE_NS + viewKey, JSON.stringify(next));
  },
  async remove(viewKey, id) {
    if (typeof window === "undefined") return;
    const list = await this.list(viewKey);
    window.localStorage.setItem(
      STORAGE_NS + viewKey,
      JSON.stringify(list.filter((v) => v.id !== id)),
    );
  },
};

// TODO(spec-004:gap:user-preferences-endpoint): swap to a server-backed
// adapter once spec 004 ships /v1/admin/me/preferences. The migration
// path is documented in `contracts/permission-catalog.md` Operations.
const activeBackend: StorageBackend = localStorageBackend;

export function SavedViewsBar<TFilter>({
  viewKey,
  currentFilter,
  onApply,
}: SavedViewsBarProps<TFilter>) {
  const t = useTranslations("common");
  const tSavedViews = useTranslations("saved_views");
  const [views, setViews] = useState<SavedView<TFilter>[]>([]);

  useEffect(() => {
    let cancelled = false;
    void activeBackend.list(viewKey).then((list) => {
      if (!cancelled) setViews(list as SavedView<TFilter>[]);
    });
    return () => {
      cancelled = true;
    };
  }, [viewKey]);

  async function saveCurrent() {
    const name = window.prompt(tSavedViews("name_prompt"));
    if (!name) return;
    const view: SavedView<TFilter> = {
      id: crypto.randomUUID(),
      name,
      filter: currentFilter,
      createdAt: new Date().toISOString(),
    };
    await activeBackend.put(viewKey, view as SavedView<unknown>);
    setViews((prev) => [...prev.filter((v) => v.id !== view.id), view]);
  }

  async function removeView(id: string) {
    await activeBackend.remove(viewKey, id);
    setViews((prev) => prev.filter((v) => v.id !== id));
  }

  return (
    <div className="flex items-center gap-ds-sm">
      <DropdownMenu>
        <DropdownMenuTrigger className="inline-flex items-center gap-ds-xs rounded-md border border-border bg-background px-ds-sm py-ds-xs text-sm hover:bg-muted">
          {tSavedViews("trigger_label")}
          <ChevronDown aria-hidden="true" className="size-4" />
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className="w-72">
          <DropdownMenuLabel>{viewKey}</DropdownMenuLabel>
          <DropdownMenuSeparator />
          {views.length === 0 ? (
            <div className="p-ds-sm text-xs text-muted-foreground">{tSavedViews("empty")}</div>
          ) : (
            views.map((v) => (
              <DropdownMenuItem
                key={v.id}
                className="flex items-center justify-between"
                onSelect={() => onApply(v.filter)}
              >
                <span>{v.name}</span>
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label={t("discard")}
                  onClick={(e) => {
                    e.stopPropagation();
                    void removeView(v.id);
                  }}
                >
                  <Trash2 aria-hidden="true" className="size-3.5" />
                </Button>
              </DropdownMenuItem>
            ))
          )}
        </DropdownMenuContent>
      </DropdownMenu>

      <Button type="button" variant="outline" size="sm" onClick={saveCurrent}>
        <Save aria-hidden="true" className="me-ds-xs size-4" />
        {t("save")}
      </Button>
    </div>
  );
}
