/**
 * Spec 025 T048 — admin dead-letter operator surface. Scaffold-only;
 * full UI is Phase 7+ work.
 */
import { PageHeader } from "@/components/shell/page-header";

export default function DeadLetterPage() {
  return (
    <div className="space-y-ds-lg">
      <PageHeader
        title="Dead-letter queue"
        description="Notifications whose retry budget exhausted. Operator may retry or discard each row."
      />
      <div className="rounded-md border border-dashed border-border p-ds-lg text-sm text-muted-foreground">
        <p>Full UI scaffolding pending. Wired backend endpoints:</p>
        <ul className="mt-ds-sm list-disc space-y-ds-xs pl-ds-md">
          <li><code>GET /admin/notifications/dead-letter</code></li>
          <li><code>POST /admin/notifications/dead-letter/{`{id}`}:retry</code></li>
          <li><code>POST /admin/notifications/dead-letter/{`{id}`}:discard</code></li>
        </ul>
        <p className="mt-ds-sm">
          Retention: resolved rows archive after 30 days (clarify-locked);
          archive purges at 365 days. Both run via <code>DeadLetterArchiver</code> hosted service.
        </p>
      </div>
    </div>
  );
}
