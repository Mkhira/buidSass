/**
 * T057a (FR-028a): /me/preferences — saved-views management UI.
 *
 * Surfaces every saved view across modules (the localStorage-backed
 * adapter for now; promotes to spec 004's user-preferences endpoint
 * when shipped per `<SavedViewsBar>` migration plan). v1 ships a
 * read-only listing — full rename / delete / reorder lands when any
 * module's saved-views feature ships in Sessions 5+.
 */
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/shell/page-header";
import { EmptyState } from "@/components/shell/empty-state";
import { PreferencesList } from "./preferences-list";

export default async function PreferencesPage() {
  const t = await getTranslations("nav.entry");
  return (
    <div className="space-y-ds-lg">
      <PageHeader title={t("preferences")} />
      <PreferencesList />
    </div>
  );
}
