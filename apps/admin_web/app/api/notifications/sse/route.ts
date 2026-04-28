/**
 * T084: GET /api/notifications/sse — SSE proxy.
 *
 * Until spec 023 ships its upstream SSE endpoint, this returns a
 * heartbeat-only stream so the BellMenu's reconnect loop stays steady
 * (no constant reconnect storm). When 023 lands, this route proxies
 * the upstream stream, attaches the iron-sealed access token, and
 * forwards the heartbeat passthrough.
 */
import type { NextRequest } from "next/server";
import { getSession } from "@/lib/auth/session";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

const HEARTBEAT_INTERVAL_MS = 30_000;

export async function GET(_req: NextRequest) {
  const session = await getSession();
  if (!session) {
    return new Response("Unauthorized", { status: 401 });
  }

  const upstreamUrl = process.env.NOTIFICATIONS_SSE_URL;
  const stream = new ReadableStream<Uint8Array>({
    start: async (controller) => {
      const encoder = new TextEncoder();
      // Initial heartbeat so the client transitions to "Connected".
      controller.enqueue(encoder.encode(`event: heartbeat\ndata: 1\n\n`));

      let upstreamCancelled = false;
      let heartbeat: ReturnType<typeof setInterval> | null = null;

      // If spec 023's endpoint exists, proxy it. Otherwise heartbeat-only.
      let proxied = false;
      if (upstreamUrl) {
        try {
          const upstream = await fetch(upstreamUrl, {
            headers: {
              Authorization: `Bearer ${session.accessToken}`,
              Accept: "text/event-stream",
            },
          });
          if (upstream.ok && upstream.body) {
            proxied = true;
            const reader = upstream.body.getReader();
            const pump = async () => {
              try {
                while (!upstreamCancelled) {
                  const { value, done } = await reader.read();
                  if (done) break;
                  controller.enqueue(value);
                }
              } catch {
                // upstream closed — fall through to heartbeat
              }
            };
            void pump();
          }
        } catch {
          // upstream unreachable — fall through to heartbeat
        }
      }

      heartbeat = setInterval(() => {
        try {
          controller.enqueue(encoder.encode(`event: heartbeat\ndata: ${Date.now()}\n\n`));
        } catch {
          if (heartbeat) clearInterval(heartbeat);
        }
      }, HEARTBEAT_INTERVAL_MS);
    },
    cancel: () => {
      // upstream pump references this via closure; the abort lands when
      // the controller cancels — best-effort.
    },
  });

  return new Response(stream, {
    headers: {
      "Content-Type": "text/event-stream",
      "Cache-Control": "no-cache, no-transform",
      Connection: "keep-alive",
      "X-Accel-Buffering": "no",
    },
  });
}
