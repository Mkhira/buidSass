/**
 * Spec 025 T048 — admin provider-routing operator surface. Scaffold-only;
 * full UI is Phase 7+ work.
 */
import { PageHeader } from "@/components/shell/page-header";

export default function ProviderRoutingPage() {
  return (
    <div className="space-y-ds-lg">
      <PageHeader
        title="Provider routing"
        description="Per-market × channel primary/backup provider with manual or threshold-driven failover."
      />
      <div className="rounded-md border border-dashed border-border p-ds-lg text-sm text-muted-foreground">
        <p>Full UI scaffolding pending. Wired backend endpoints:</p>
        <ul className="mt-ds-sm list-disc space-y-ds-xs pl-ds-md">
          <li><code>GET /admin/notifications/provider-routing/{`{market}`}/{`{channel}`}</code></li>
          <li><code>PUT /admin/notifications/provider-routing/{`{market}`}/{`{channel}`}</code></li>
          <li><code>POST /admin/notifications/provider-routing/{`{market}`}/{`{channel}`}:failover</code></li>
        </ul>
        <p className="mt-ds-sm">
          AutoFailoverEnabled defaults to <code>false</code> at v1 (clarify-locked) — operators opt in per row.
          <code>ProviderHealthMonitor</code> emits <code>provider.degraded</code> when the 5-min
          failure-rate window crosses the configured threshold.
        </p>
      </div>
    </div>
  );
}
