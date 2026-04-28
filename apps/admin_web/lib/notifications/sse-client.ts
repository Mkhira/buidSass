/**
 * T083 (FR-026): SSE client wrapper.
 *
 * Consumes `/api/notifications/sse` (which proxies spec 023's upstream
 * stream when shipped). Reconnects with exponential backoff up to 30 s,
 * then falls back to 60 s polling on the `/api/notifications/unread`
 * endpoint until SSE re-establishes.
 *
 * The client emits structured callbacks the BellMenu Client Component
 * subscribes to:
 *   - onMessage(notification)   — new notification arrived
 *   - onConnect()               — SSE first message
 *   - onReconnectAttempt(n)     — reconnect attempt
 *   - onFallbackToPolling()     — exponential backoff exhausted
 */
"use client";

import { createParser, type EventSourceMessage } from "eventsource-parser";
import { emitTelemetry } from "@/lib/observability/telemetry";

export interface SseHandlers {
  onMessage(payload: unknown): void;
  onConnect?(): void;
  onReconnectAttempt?(n: number): void;
  onFallbackToPolling?(): void;
}

export interface SseClient {
  start(): void;
  stop(): void;
}

const SSE_URL = "/api/notifications/sse";
const POLL_FALLBACK_URL = "/api/notifications/unread";
const MAX_BACKOFF_MS = 30_000;
const POLL_INTERVAL_MS = 60_000;

export function createSseClient(handlers: SseHandlers): SseClient {
  let controller: AbortController | null = null;
  let pollTimer: ReturnType<typeof setTimeout> | null = null;
  let reconnectAttempts = 0;
  let stopped = false;

  async function readStream() {
    if (stopped) return;
    controller = new AbortController();
    try {
      const res = await fetch(SSE_URL, {
        signal: controller.signal,
        credentials: "same-origin",
        headers: { Accept: "text/event-stream" },
      });
      if (!res.ok || !res.body) throw new Error(`http_${res.status}`);

      reconnectAttempts = 0;
      handlers.onConnect?.();
      emitTelemetry({ name: "admin.bell.sse.connected" });

      const parser = createParser({
        onEvent: (event: EventSourceMessage) => {
          if (event.event === "heartbeat") return;
          try {
            handlers.onMessage(JSON.parse(event.data));
          } catch {
            // ignore malformed events
          }
        },
      });

      const reader = res.body.getReader();
      const decoder = new TextDecoder();
      while (!stopped) {
        const { value, done } = await reader.read();
        if (done) break;
        parser.feed(decoder.decode(value, { stream: true }));
      }
    } catch {
      if (stopped) return;
      reconnectAttempts++;
      handlers.onReconnectAttempt?.(reconnectAttempts);
      emitTelemetry({
        name: "admin.bell.sse.reconnect_attempt",
        properties: { attempt_n: reconnectAttempts },
      });
      const backoff = Math.min(2 ** reconnectAttempts * 1000, MAX_BACKOFF_MS);
      if (backoff >= MAX_BACKOFF_MS) {
        handlers.onFallbackToPolling?.();
        emitTelemetry({ name: "admin.bell.sse.fallback_to_polling" });
        startPolling();
        return;
      }
      setTimeout(readStream, backoff);
    }
  }

  function startPolling() {
    if (stopped || pollTimer) return;
    const tick = async () => {
      if (stopped) return;
      try {
        const res = await fetch(POLL_FALLBACK_URL, { credentials: "same-origin" });
        if (res.ok) {
          const body = (await res.json()) as {
            entries: unknown[];
            unreadCount: number;
          };
          for (const entry of body.entries) {
            handlers.onMessage(entry);
          }
        }
      } catch {
        // best-effort
      }
      // Retry SSE periodically
      if (Math.random() < 0.3) {
        stopPolling();
        readStream();
        return;
      }
      pollTimer = setTimeout(tick, POLL_INTERVAL_MS);
    };
    pollTimer = setTimeout(tick, POLL_INTERVAL_MS);
  }

  function stopPolling() {
    if (pollTimer) clearTimeout(pollTimer);
    pollTimer = null;
  }

  return {
    start() {
      stopped = false;
      void readStream();
    },
    stop() {
      stopped = true;
      controller?.abort();
      stopPolling();
    },
  };
}
