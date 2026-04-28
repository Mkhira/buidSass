/**
 * T030: Stub feed for the notification bell until spec 023 ships its
 * server-side endpoints + SSE stream.
 *
 * Per spec 015 Assumptions: "Until 023 ships, the bell is wired to a stub
 * feed (single seeded entry 'Welcome — admin notifications go here when
 * spec 023 lands') so the affordance is discoverable and the wiring is
 * exercised."
 */
import { proxyFetch } from "@/lib/api/proxy";

export interface NotificationEntry {
  id: string;
  kindKey: string;
  titleKey: string;
  bodyKey: string;
  deepLink: string;
  occurredAt: string;
  read: boolean;
}

export const notificationsApi = {
  unread: async (): Promise<{ entries: NotificationEntry[]; unreadCount: number }> => {
    // While spec 023 hasn't shipped, return a single seeded "welcome"
    // entry so the bell affordance is discoverable.
    const stub = process.env.NEXT_PUBLIC_NOTIFICATIONS_STUB === "1";
    if (stub) {
      return {
        unreadCount: 1,
        entries: [
          {
            id: "stub-welcome",
            kindKey: "notifications.stub.welcome",
            titleKey: "shell.topbar.notifications",
            bodyKey: "shell.topbar.notifications",
            deepLink: "/me",
            occurredAt: new Date().toISOString(),
            read: false,
          },
        ],
      };
    }
    return proxyFetch<{ entries: NotificationEntry[]; unreadCount: number }>(
      "/v1/admin/notifications/unread",
    );
  },

  markRead: (id: string) =>
    proxyFetch<void>(`/v1/admin/notifications/${encodeURIComponent(id)}/read`, {
      method: "POST",
    }),
};
