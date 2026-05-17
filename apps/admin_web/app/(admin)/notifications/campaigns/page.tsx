/**
 * Spec 025 T040 — admin campaigns. Scaffold-only delivery; full UI lands
 * in the Phase 7+ UI batch.
 */
import { PageHeader } from "@/components/shell/page-header";

export default function CampaignsPage() {
  return (
    <div className="space-y-ds-lg">
      <PageHeader
        title="Campaigns"
        description="Schedule, pause, resume, cancel campaigns and read per-state delivery reports."
      />
      <div className="rounded-md border border-dashed border-border p-ds-lg text-sm text-muted-foreground">
        <p>Full UI scaffolding pending. Wired backend endpoints:</p>
        <ul className="mt-ds-sm list-disc space-y-ds-xs pl-ds-md">
          <li><code>POST /admin/notifications/campaigns</code> — create draft</li>
          <li><code>POST /admin/notifications/campaigns/{`{id}`}:schedule</code></li>
          <li><code>POST /admin/notifications/campaigns/{`{id}`}:{`{pause,resume,cancel}`}</code></li>
          <li><code>GET /admin/notifications/campaigns/{`{id}`}/report</code></li>
        </ul>
      </div>
    </div>
  );
}
