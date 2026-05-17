/**
 * Spec 025 T019 — admin templates list + editor + review board.
 *
 * Scaffold-only delivery. The full UI (list with search/filter, AR/EN editor
 * with placeholder validation surfaced inline, review board with V-1 gate
 * indicators) is Phase 7+ UI work. The page renders a placeholder grid so
 * navigation routing is testable end-to-end against the wired-up endpoints
 * (POST /admin/notifications/templates*).
 */
import { PageHeader } from "@/components/shell/page-header";

export default function TemplatesPage() {
  return (
    <div className="space-y-ds-lg">
      <PageHeader
        title="Templates"
        description="AR + EN template versions per event kind. Editor + review board UI is Phase 7+ work."
      />
      <div className="rounded-md border border-dashed border-border p-ds-lg text-sm text-muted-foreground">
        <p>Full UI scaffolding pending. Wired backend endpoints:</p>
        <ul className="mt-ds-sm list-disc space-y-ds-xs pl-ds-md">
          <li><code>POST /admin/notifications/templates</code> — create draft</li>
          <li><code>POST /admin/notifications/templates/{`{id}`}:submit</code> — submit for review</li>
          <li><code>POST /admin/notifications/templates/{`{id}`}:approve</code> — V-1 publish gate</li>
          <li><code>POST /admin/notifications/templates/{`{id}`}:reject</code> — reject with comment</li>
          <li><code>POST /admin/notifications/templates/{`{id}`}:archive</code> — archive</li>
        </ul>
      </div>
    </div>
  );
}
