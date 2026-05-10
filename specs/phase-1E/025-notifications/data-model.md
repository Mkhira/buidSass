# Data Model: 025 — Notifications

**Phase**: 1 (Design)
**Date**: 2026-05-10

12 new tables under the `notifications` schema. All inherit the four mandatory columns from spec 003 (`created_at`, `updated_at`, `deleted_at`, `market_code` where applicable). All FKs use `ON DELETE RESTRICT` (BR-10 hard-delete forbidden). Soft-delete via query filter.

## Tables

### `notifications.templates`
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| event_kind | text | indexed; controlled enum |
| current_version_id | uuid FK → template_versions(id) | nullable until first publish |
| state | text | derived from current version's state |
| created_at, updated_at, deleted_at | timestamptz | |

Index: `(event_kind, deleted_at)` partial.

### `notifications.template_versions`
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| template_id | uuid FK | |
| version_no | int | unique within template; auto-incremented |
| state | text | `draft`, `in_review`, `published`, `archived` |
| body_ar | text NOT NULL | AR body |
| body_en | text NOT NULL | EN body |
| subject_ar | text | nullable for SMS/push |
| subject_en | text | nullable for SMS/push |
| placeholders | jsonb NOT NULL DEFAULT '[]' | list of placeholder names |
| ar_editorial_reviewed | boolean NOT NULL DEFAULT false | V-1 publish gate |
| author_id | uuid FK → auth.users | |
| reviewer_id | uuid FK → auth.users | nullable until review |
| submitted_at, published_at, archived_at | timestamptz | nullable |
| created_at, updated_at, deleted_at | timestamptz | |

Constraint: `published_at IS NOT NULL` ⇒ `state IN ('published','archived')` AND `ar_editorial_reviewed = true` AND `reviewer_id IS NOT NULL` AND `reviewer_id <> author_id`.

### `notifications.notifications`
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| correlation_id | uuid | source-event correlation id |
| recipient_id | uuid | nullable for anonymous (rare) |
| recipient_kind | text | `customer`, `admin`, `anonymous` |
| channel | text | `sms`, `email`, `push` |
| event_kind | text | matches templates.event_kind or `campaign:<campaign_id>` |
| template_version_id | uuid FK → template_versions(id) | snapshot reference (BR-8) |
| market_code | text | `sa`, `eg` |
| locale | text | `ar`, `en` |
| state | text | `pending`, `queued`, `sending`, `delivered`, `failed`, `retrying`, `dead_letter`, `skipped` |
| skipped_reason | text | nullable; populated when state=skipped |
| failed_reason | text | nullable; populated when state=failed/dead_letter |
| provider_id | text | resolved at send time; nullable when skipped |
| provider_message_id | text | populated by provider response |
| attempts | int DEFAULT 0 | |
| idempotency_key | text NOT NULL | sha256-derived |
| payload_redacted_jsonb | jsonb | rendered + PII-redacted (AC-27) |
| campaign_id | uuid FK → campaigns(id) | nullable for non-campaign |
| delivered_at, failed_at | timestamptz | nullable |
| created_at, updated_at, deleted_at | timestamptz | |

Indexes: `(state, channel)` partial WHERE state IN ('pending','queued','retrying'), `(idempotency_key) WHERE deleted_at IS NULL` UNIQUE, `(recipient_id, created_at DESC)`, `(campaign_id, state)`, `(provider_id, provider_message_id)`.

### `notifications.deliveries`
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| notification_id | uuid FK | |
| attempt_no | int | 1-based |
| provider_id | text | |
| provider_message_id | text | nullable on transient pre-call failure |
| status | text | provider's terminal status (`accepted`, `delivered`, `bounced`, `failed`, `timeout`) |
| error_code | text | provider error code (nullable on success) |
| error_message_redacted | text | nullable; PII-stripped |
| requested_at, responded_at | timestamptz | |
| created_at | timestamptz | |

Index: `(notification_id, attempt_no)`.

### `notifications.webhooks_received`
| Column | Type | Notes |
|---|---|---|
| provider_id | text | composite PK |
| provider_message_id | text | composite PK |
| event_kind | text | `delivered`, `bounced`, `unregistered`, etc. |
| received_at | timestamptz | |
| signature_validated | boolean NOT NULL | always true for accepted rows |

PK: `(provider_id, provider_message_id, event_kind)`. This is the idempotency surface (V-6).

### `notifications.campaigns`
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| name | text NOT NULL | |
| state | text | `draft`, `scheduled`, `sending`, `paused`, `completed`, `cancelled` |
| template_id | uuid FK | |
| template_version_id | uuid FK | snapshot at schedule time |
| channel | text | `sms`, `email`, `push` (NEVER `otp`) |
| target_criteria_jsonb | jsonb NOT NULL | segment definition |
| send_at | timestamptz | nullable until scheduled |
| created_by | uuid FK → auth.users | |
| recipient_count_snapshot | int | populated when state moves to `sending` |
| started_at, completed_at, paused_at, cancelled_at | timestamptz | nullable |
| created_at, updated_at, deleted_at | timestamptz | |

Constraint: `channel <> 'otp'`.

### `notifications.campaign_recipients`
| Column | Type | Notes |
|---|---|---|
| campaign_id | uuid FK | composite PK |
| recipient_id | uuid | composite PK |
| notification_id | uuid FK | nullable when skipped |
| skipped_reason | text | nullable; one of `channel_disabled_by_customer`, `rate_limited`, `recipient_deactivated` |
| materialized_at | timestamptz | |

PK: `(campaign_id, recipient_id)`.

### `notifications.preferences`
| Column | Type | Notes |
|---|---|---|
| customer_id | uuid FK → auth.users | composite PK |
| channel | text | composite PK; `sms`, `email`, `push` |
| category | text | composite PK; `transactional`, `marketing` |
| enabled | boolean NOT NULL | default true |
| updated_at | timestamptz | |

PK: `(customer_id, channel, category)`. Constraint: `(category='transactional' AND enabled=true)` cannot be set to false (V-4 enforced at app layer; DB constraint enforced via trigger).

### `notifications.unsubscribe_tokens`
| Column | Type | Notes |
|---|---|---|
| token_hash | bytea PK | SHA-256 of the signed token |
| customer_id | uuid FK | |
| channel | text | |
| category | text | always `marketing` |
| expires_at | timestamptz | now() + interval '30 days' |
| used_at | timestamptz | nullable |

### `notifications.provider_routing`
| Column | Type | Notes |
|---|---|---|
| market_code | text | composite PK |
| channel | text | composite PK; `sms`, `email`, `push` |
| primary_provider_id | text NOT NULL | |
| backup_provider_id | text | nullable |
| auto_failover_enabled | boolean NOT NULL DEFAULT false | clarify-locked default |
| failover_threshold_pct | int NOT NULL DEFAULT 50 | range [10,90] |
| failover_window_minutes | int NOT NULL DEFAULT 5 | |
| updated_at | timestamptz | |

PK: `(market_code, channel)`. Constraint: `primary_provider_id <> backup_provider_id`.

### `notifications.dead_letter_queue`
| Column | Type | Notes |
|---|---|---|
| notification_id | uuid PK FK | |
| last_error_message_redacted | text | |
| entered_at | timestamptz | |
| resolved_at | timestamptz | nullable |
| resolution | text | nullable; `retry`, `discard` |
| resolved_by | uuid FK | nullable |

`notifications.dead_letter_queue_archive` has the same shape; the archiver moves rows older than 30 days.

### `notifications.market_schemas`
| Column | Type | Notes |
|---|---|---|
| market_code | text PK | `sa`, `eg` |
| quiet_hours_marketing_local_start | time NOT NULL | e.g., '22:00' |
| quiet_hours_marketing_local_end | time NOT NULL | e.g., '08:00' |
| quiet_hours_timezone | text NOT NULL | `Asia/Riyadh`, `Africa/Cairo` |
| unsubscribe_footer_ar | text NOT NULL | editorial AR |
| unsubscribe_footer_en | text NOT NULL | |
| rate_limit_marketing_per_24h | int NOT NULL DEFAULT 1 | per recipient per channel |
| rate_limit_transactional_per_24h | int NOT NULL DEFAULT 5 | |
| updated_at | timestamptz | |

## State machines

(Full transition tables in spec.md.)

| Domain | States |
|---|---|
| Notification | `pending → queued → sending → delivered ∪ failed → retrying → (loop) → dead_letter ∪ skipped` |
| TemplateVersion | `draft → in_review → published ↔ archived` |
| Campaign | `draft → scheduled → sending → completed ∪ paused → sending ∪ cancelled` |

## Audit-event additions to `audit_log_entries` (additive event types)

| Event | Mandatory payload |
|---|---|
| `notification.created` | `notification_id`, `correlation_id`, `channel`, `recipient_id`, `event_kind`, `market_code` |
| `notification.delivered` | `notification_id`, `provider_id`, `provider_message_id`, `delivered_at` |
| `notification.failed` | `notification_id`, `attempts`, `error_code`, `error_message_redacted` |
| `notification.dead_letter` | `notification_id`, `final_error_code`, `final_error_message_redacted` |
| `notification.skipped` | `notification_id`, `skipped_reason` |
| `template.submitted_for_review` | `template_id`, `version_id`, `author_id` |
| `template.published` | `template_id`, `version_id`, `reviewer_id`, `previous_version_id` |
| `template.archived` | `template_id`, `version_id`, `actor_id` |
| `campaign.scheduled` | `campaign_id`, `send_at`, `target_count_estimate` |
| `campaign.sending_started` | `campaign_id`, `recipient_count_snapshot` |
| `campaign.paused` | `campaign_id`, `actor_id` |
| `campaign.cancelled` | `campaign_id`, `actor_id` |
| `campaign.completed` | `campaign_id`, `delivered_count`, `bounced_count`, `skipped_count` |
| `preference.changed` | `customer_id`, `channel`, `category`, `old_enabled`, `new_enabled`, `actor_kind` |
| `preference.opt_out` | `customer_id`, `channel`, `via_token` (boolean) |
| `preference.opt_out_signature_invalid` | `token_hash_prefix`, `client_ip` |
| `provider.degraded` | `provider_id`, `failure_rate_pct`, `window_minutes` |
| `provider.failover` | `from_provider_id`, `to_provider_id`, `market_code`, `channel`, `actor_kind` |
| `secret.placeholder_replaced` | inherited from E1 — emitted when 025 overwrites a sentinel slot |

All retained ≥ 365 days.

## Cross-references

- `notifications.notifications.recipient_id` → `auth.users.id` when `recipient_kind='customer'`.
- `notifications.notifications.template_version_id` → snapshot reference (immutable per BR-8).
- `notifications.preferences.customer_id` → `auth.users.id`.
- E1 Key Vault slots populated by 025: `notifications-email/multi/ses/api-key`, `notifications-email/multi/sendgrid/api-key`, `notifications-sms/sa/unifonic/api-key`, `notifications-sms/sa/infobip/api-key`, `notifications-sms/eg/vodafone-egypt/api-key`, `notifications-sms/eg/infobip/api-key`, `notifications-push/multi/fcm/service-account-json`. Each population emits `secret.placeholder_replaced`.
