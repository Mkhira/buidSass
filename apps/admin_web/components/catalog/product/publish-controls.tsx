/**
 * T029 — PublishControls.
 * Renders the publish / schedule / discard / revert actions, gated by
 * SM-1's `allowedTransitions(state)`.
 */
"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  allowedTransitions,
  type ClientProductState,
} from "@/lib/catalog/product-state";
import type { ProductState } from "@/lib/api/clients/catalog";

export interface PublishControlsProps {
  state: ClientProductState;
  isSubmitting?: boolean;
  onPublish: (scheduledAt?: string) => void;
  onDiscard: () => void;
  onTransitionTo: (target: ProductState) => void;
  onSaveDraft: () => void;
}

export function PublishControls(props: PublishControlsProps) {
  const t = useTranslations("catalog.product.publish");
  const [showSchedule, setShowSchedule] = useState(false);
  const [scheduledAt, setScheduledAt] = useState("");
  const transitions = allowedTransitions(props.state);

  return (
    <div className="flex flex-wrap items-end gap-ds-sm">
      <Button
        type="button"
        variant="secondary"
        onClick={props.onSaveDraft}
        disabled={props.isSubmitting}
      >
        {t("save_draft")}
      </Button>
      {transitions.map((tr) => {
        if (tr.actionKey === "schedule_publish") {
          return (
            <div key={tr.actionKey} className="flex items-end gap-ds-xs">
              {showSchedule ? (
                <>
                  <div>
                    <Label htmlFor="scheduled-at">{t("schedule_at_label")}</Label>
                    <Input
                      id="scheduled-at"
                      type="datetime-local"
                      value={scheduledAt}
                      onChange={(e) => setScheduledAt(e.target.value)}
                    />
                  </div>
                  <Button
                    type="button"
                    onClick={() =>
                      props.onPublish(
                        scheduledAt
                          ? new Date(scheduledAt).toISOString()
                          : undefined,
                      )
                    }
                    disabled={!scheduledAt || props.isSubmitting}
                  >
                    {t("schedule_publish")}
                  </Button>
                </>
              ) : (
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => setShowSchedule(true)}
                >
                  {t("schedule_publish")}
                </Button>
              )}
            </div>
          );
        }
        if (tr.actionKey === "publish_now") {
          return (
            <Button
              key={tr.actionKey}
              type="button"
              onClick={() => props.onPublish()}
              disabled={props.isSubmitting}
            >
              {t("publish_now")}
            </Button>
          );
        }
        return (
          <Button
            key={tr.actionKey}
            type="button"
            variant={tr.actionKey === "discard" ? "destructive" : "outline"}
            onClick={() => props.onTransitionTo(tr.to)}
            disabled={props.isSubmitting}
          >
            {t(tr.actionKey)}
          </Button>
        );
      })}
      <Button
        type="button"
        variant="ghost"
        onClick={props.onDiscard}
        disabled={props.isSubmitting}
      >
        {t("discard")}
      </Button>
    </div>
  );
}
