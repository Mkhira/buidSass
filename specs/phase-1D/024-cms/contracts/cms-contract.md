# CMS Contract

**Phase**: 1 (alongside data-model.md and quickstart.md)
**Date**: 2026-04-29
**Module**: `Modules/Cms/` — vertical-slice .NET module under the modular monolith.

This document is the authoritative wire contract for spec 024. It enumerates every endpoint, its RBAC permission, request / response shape, status codes, reason codes, and the cross-module interfaces consumed or published. The companion OpenAPI document at `services/backend_api/openapi.cms.json` (regenerated in Phase Q of plan.md) MUST be congruent with this file before merge.

---

## §1. RBAC permissions

New in this spec (registered by spec 015 onto each user's role bindings):

| Permission | Held by | Purpose |
|---|---|---|
| `cms.editor` | `cms.editor`, `cms.publisher`, `cms.legal_owner`, `super_admin` | Author + edit drafts of banner / featured / FAQ / blog kinds; mint preview tokens; dismiss own stale-alerts |
| `cms.publisher` | `cms.publisher`, `super_admin` | Publish + archive banner / featured / FAQ / blog kinds; bind / unbind banner-campaign; reassign draft ownership |
| `cms.legal_owner` | `cms.legal_owner`, `super_admin` | Author + publish per-market legal page versions |
| `cms.super_admin` | `super_admin` only | Publish `*`-scoped legal page versions; edit `cms.market_schemas`; redact-style asset moderation; list orphaned assets |
| `cms.viewer.finance` | `viewer.finance` (existing) | Read-only on legal page version-history |
| `cms.viewer.b2b` | `b2b.account_manager` (existing) | Read-only on banners + featured + blog (relationship visibility) |

Storefront read endpoints are `[AllowAnonymous]` per Principle 3 and Research §R19; they use a per-IP rate-limit policy `cms-storefront` instead.

---

## §2. URL conventions

- Admin authoring: `POST /v1/admin/cms/{kind}/...`
- Storefront read: `GET /v1/storefront/cms/{kind}/...`
- Preview-token read: `GET /v1/storefront/cms/preview/{kind}/{id}?token=...`

`{kind}` ∈ `banner-slots | featured-sections | faq-entries | blog-articles | legal-pages`. The "`legal-pages`" kind addresses the page (terms / privacy / returns / cookies) — versions are addressed by `/legal-pages/{kind}/versions/{version_id}` where `kind` ∈ `terms | privacy | returns | cookies`.

All admin POSTs require `Idempotency-Key` (24 h replay window per spec 003 platform middleware).

---

## §3. Editor endpoints (15)

### §3.1 `POST /v1/admin/cms/banner-slots/drafts`

**Permission**: `cms.editor`. **Idempotency**: required. **Concurrency**: xmin row_version (read-modify-write).

**Request**:
```json
{
  "slot_kind": "hero_top",
  "headline_ar": "...", "headline_en": "...",
  "subhead_ar": "...", "subhead_en": "...",
  "asset_id_ar": "<uuid>", "asset_id_en": "<uuid>",
  "cta_kind": "category", "cta_target": "<uuid>",
  "scheduled_start_utc": "2026-05-15T20:00:00Z",
  "scheduled_end_utc":   "2026-05-25T20:00:00Z",
  "market_code": "KSA",
  "priority_within_slot": 100
}
```

**201 Response**: full `BannerSlot` entity row, `state=draft`, `xmin`, `created_at_utc`.

**Errors**:
- `400 cms.banner.schedule_window_invalid` — start ≥ end.
- `400 cms.banner.cta_kind_target_mismatch` — e.g., `cta_kind=product` with non-UUID `cta_target`.
- `400 cms.banner.external_url_https_required` — `cta_kind=external_url` with non-https.
- `400 cms.asset.mime_forbidden` — referenced asset MIME not allowed.
- `403 cms.editor.role_required`.
- `429 cms.admin_rate_limit_exceeded` (60/min/actor).

### §3.2 `PATCH /v1/admin/cms/banner-slots/drafts/{id}`

Updates a `draft` row. Same body shape; xmin guard. Rejects if `state != 'draft'` with `400 cms.draft.not_editable`.

**Errors**: `409 cms.draft.version_conflict` on xmin race.

### §3.3 `POST /v1/admin/cms/featured-sections/drafts`

**Permission**: `cms.editor`. Mirrors §3.1 with the featured-section schema.

**Request body**:
```json
{
  "section_kind": "home_top",
  "title_ar": "...", "title_en": "...",
  "subtitle_ar": "...", "subtitle_en": "...",
  "references": [
    {"kind": "product", "id": "<uuid>"},
    {"kind": "category", "id": "<uuid>"},
    {"kind": "bundle", "id": "<uuid>"}
  ],
  "display_priority": 100,
  "market_code": "EG",
  "scheduled_publish_at_utc": "2026-05-01T08:00:00Z"
}
```

**Errors**:
- `400 cms.featured_section.empty_references` — empty array.
- `400 cms.featured_section.too_many_references` — > `CmsMarketSchema.featured_section_max_references` (V1 default 24).
- `400 cms.featured_section.reference_kind_unsupported` — kind ∉ {product, category, bundle}.

### §3.4 `PATCH /v1/admin/cms/featured-sections/drafts/{id}`

### §3.5 `POST /v1/admin/cms/faq-entries/drafts`

**Permission**: `cms.editor`.

**Request**:
```json
{
  "category": "ordering",
  "question_ar": "...", "question_en": "...",
  "answer_ar": "...", "answer_en": "...",
  "display_order": 10,
  "market_code": "EG"
}
```

### §3.6 `PATCH /v1/admin/cms/faq-entries/drafts/{id}`

### §3.7 `POST /v1/admin/cms/faq-entries/reorder` (bulk)

**Permission**: `cms.editor`. Idempotent on `Idempotency-Key`. xmin-guarded per-row.

**Request**:
```json
{
  "market_code": "EG",
  "category": "ordering",
  "entries": [
    {"id": "<uuid>", "display_order": 10, "xmin": "..."},
    {"id": "<uuid>", "display_order": 20, "xmin": "..."}
  ]
}
```

**200 Response**: updated rows with new `xmin`.

**Errors**: `409 cms.faq.reorder_conflict` if any row's xmin doesn't match (none of the rows in the batch are updated; the loser sees the current state and re-merges in the editor UI).

### §3.8 `POST /v1/admin/cms/blog-articles/drafts`

**Permission**: `cms.editor`. Same envelope, blog schema.

**Errors**:
- `400 cms.blog.slug_collision` — slug not unique per `(market_code, authored_locale)`.
- `400 cms.blog.slug_invalid_pattern` — slug does not match `^[a-z0-9]+(-[a-z0-9]+)*$`.
- `400 cms.blog.body_too_long` — > 60 000 chars.

### §3.9 `PATCH /v1/admin/cms/blog-articles/drafts/{id}`

### §3.10 `DELETE /v1/admin/cms/{kind}/drafts/{id}`

**Permission**: `cms.editor` (creator) or `super_admin`. Idempotent. Rate-limited 30/h/actor.

**200 Response**: `{deleted_at_utc: "..."}`. Audited; emits `cms.draft.deleted` and `cms.asset.dereferenced` for any asset_ids on the deleted row.

**Errors**:
- `405 cms.{kind}.delete_forbidden` — row is not in `draft` state (FR-005a).
- `403 cms.draft.delete_not_owner` — actor is neither the creator nor `super_admin`.
- `429 cms.admin_rate_limit_exceeded`.

### §3.11 `GET /v1/admin/cms/drafts`

**Permission**: `cms.editor`. Lists drafts authored by `actor_id`. Filters: `entity_kind`, `market_code`, `stale=true`, `ownership_orphaned=true`. Page 50, max 200.

### §3.12 `POST /v1/admin/cms/drafts/{id}/dismiss-stale-alert`

**Permission**: `cms.editor` (owner) or `cms.publisher`. Idempotent. Audited.

**Request**: `{reason_note: "still drafting Q2 launch"}` (`reason_note ≥ 10 chars`).

**200 Response**: `{dismissed_at_utc: "...", re_alert_at_utc: "..."}` (re-alerts after `CmsMarketSchema.draft_staleness_alert_days` days from dismissal).

### §3.13 `POST /v1/admin/cms/{kind}/{id}/preview-token`

**Permission**: `cms.editor` (any draft) / `cms.publisher` / `cms.legal_owner`. Idempotent. Rate-limited 30/h/actor. Audited.

**Request**:
```json
{ "ttl_hours": 24 }
```

**201 Response**:
```json
{
  "token": "<opaque-base64url-160bytes>",
  "url": "https://storefront.example.com/preview/banner-slot/<id>?token=<...>",
  "expires_at_utc": "2026-04-30T12:00:00Z",
  "token_hash": "<sha256-hex>"
}
```

**Errors**:
- `400 cms.preview.ttl_out_of_range` — `ttl_hours` not in [1, 168].
- `400 cms.preview.entity_not_draftable` — entity already `archived` / `superseded`.

### §3.14 `DELETE /v1/admin/cms/preview-token/{token_hash}`

**Permission**: token minter, `cms.publisher`, `cms.legal_owner`, or `super_admin`. Idempotent. Audited.

**200 Response**: `{revoked_at_utc: "..."}`.

**Errors**: `404 cms.preview.token_not_found`; `409 cms.preview.token_already_revoked` (idempotent re-call returns 200 with original revoke timestamp).

### §3.15 `GET /v1/admin/cms/{kind}/{id}` (admin read)

**Permission**: any authenticated CMS role. Returns the full row including draft / scheduled / archived states (admin reads bypass storefront filter). The `cta_health` field on banner rows surfaces the latest read-time validation result.

---

## §4. Publisher endpoints (6)

### §4.1 `POST /v1/admin/cms/{kind}/{id}/publish-now`

**Permission**: `cms.publisher` for banner / featured / FAQ / blog; `cms.legal_owner` for legal page version. Idempotent.

**200 Response**: full row, `state=live`, `published_at_utc` stamped.

**Errors**:
- `400 cms.publish.locale_completeness_missing` — per FR-007.
- `400 cms.banner.cta_target_unresolvable` — banner CTA resolution failed at publish.
- `400 cms.banner.slot_capacity_exceeded` — per FR-021a; response includes the current `live` banners in that slot.
- `400 cms.featured_section.empty_references` — at publish time some refs may have gone unavailable.
- `400 cms.publish.effective_at_required` — legal page version missing `effective_at_utc`.
- `403 cms.publish.role_required` — actor lacks publisher role.
- `403 cms.legal_page.publish.role_required` — actor lacks legal_owner role.
- `403 cms.legal_page.cross_market_requires_super_admin` — `*`-scoped legal page publish.
- `409 cms.draft.version_conflict` — xmin race against another save.

### §4.2 `POST /v1/admin/cms/{kind}/{id}/schedule-publish`

**Permission**: same as §4.1.

**Request**:
```json
{ "scheduled_publish_at_utc": "2026-05-15T20:00:00Z" }
```
(Banner schedules use the existing `scheduled_start_utc` / `scheduled_end_utc` from the row; this endpoint just flips state to `scheduled` after gates pass.)

**200 Response**: full row, `state=scheduled`.

**Errors**: same locale-completeness + capacity + CTA + role gates as §4.1.

### §4.3 `POST /v1/admin/cms/{kind}/{id}/archive`

**Permission**: `cms.publisher` for banner / featured / FAQ / blog; `cms.legal_owner` for legal page version (rare — superseded is the natural terminal). Idempotent.

**Request**:
```json
{ "archive_reason_note": "Q1 campaign ended; replaced by Q2." }
```
(`reason_note ≥ 10 chars`.)

**200 Response**: full row, `state=archived`, `archived_at_utc` stamped.

**Errors**:
- `400 cms.archive.reason_note_required`.
- `405 cms.{kind}.archive_forbidden_in_state` — already `archived` / `superseded` / `draft`.
- `409 cms.banner.archive_blocked_by_campaign_binding` — active `BannerCampaignBinding` exists; response includes the bound `campaign_id`.

### §4.4 `POST /v1/admin/cms/banner-slots/{id}/bind-campaign`

**Permission**: `cms.publisher`. Idempotent.

**Request**:
```json
{ "campaign_id": "<uuid>" }
```

**201 Response**: `BannerCampaignBinding` row.

**Errors**: `400 cms.banner.campaign_already_bound` — banner already has an active binding (1:1 in V1).

### §4.5 `POST /v1/admin/cms/banner-slots/{id}/unbind-campaign`

**Permission**: `cms.publisher`. Idempotent.

**200 Response**: `BannerCampaignBinding` row with `released_at_utc` + `binding_state=released_by_editor`.

### §4.6 `POST /v1/admin/cms/drafts/{id}/reassign-ownership`

**Permission**: `cms.publisher`. Idempotent. Audited.

**Request**:
```json
{ "new_owner_actor_id": "<uuid>", "reason_note": "Original owner offboarded; reassigning to incoming Q2 campaign lead." }
```

**200 Response**: row with new `owner_actor_id` + `ownership_orphaned=false`.

**Errors**: `403 cms.draft.reassign.role_required`; `404 cms.draft.target_actor_not_a_cms_editor`.

---

## §5. LegalOwner endpoints (4) — covered by §3.5/§3.6/§4.1/§4.2 with role-specific gates

For legal page versions, the §3 / §4 endpoints route through `LegalOwner/` slices instead of `Editor/` / `Publisher/` slices. The `kind` in URL is `legal-pages/{legal_page_kind}/versions` (e.g., `/v1/admin/cms/legal-pages/privacy/versions/drafts`).

### §5.1 `GET /v1/admin/cms/legal-pages/{legal_page_kind}/versions`

**Permission**: `cms.legal_owner`, `cms.viewer.finance`, `super_admin`.

**200 Response**: ordered list of all versions (live + scheduled + draft + superseded) for the requested `(legal_page_kind, market_code)`. Default sort by `effective_at_utc DESC`. Includes `superseded_at_utc` and `superseded_by_version_id` on superseded rows.

---

## §6. SuperAdmin endpoints (3)

### §6.1 `POST /v1/admin/cms/legal-pages/{legal_page_kind}/versions/{id}/publish-cross-market`

**Permission**: `super_admin` only. Idempotent. Audited.

For `*`-scoped legal page versions only. Applies the same locale-completeness + effective-at gates as §4.1.

**Errors**: `403 cms.legal_page.cross_market_requires_super_admin`; `400 cms.legal_page.scope_not_cross_market` if `market_code != '*'`.

### §6.2 `PATCH /v1/admin/cms/market-schemas/{market_code}`

**Permission**: `super_admin` only. Idempotent. xmin-guarded. Audited.

**Request body**: any subset of `banner_max_live_per_slot`, `featured_section_max_references`, `preview_token_default_ttl_hours`, `draft_staleness_alert_days`, `asset_grace_period_days`.

**Errors**: `400 cms.market_schema.value_out_of_range`; `409 cms.market_schema.version_conflict`.

### §6.3 `GET /v1/admin/cms/orphaned-assets`

**Permission**: `super_admin`. Returns dereferenced assets currently in grace window (FR-009a observability).

---

## §7. Storefront endpoints (6) — `[AllowAnonymous]`

All storefront endpoints rate-limited per IP per `entity_kind` (V1 default 600 req/min/IP). Cache headers: `Cache-Control: public, max-age=60, stale-while-revalidate=300`; stable `ETag`.

### §7.1 `GET /v1/storefront/cms/banner-slots`

**Query params**: `market` (required, ∈ {EG, KSA}), `locale` (required, ∈ {ar, en}), `slot_kind?`, `page?` (default 1), `page_size?` (default 50, max 200).

**200 Response**:
```json
{
  "items": [
    {
      "id": "<uuid>",
      "slot_kind": "hero_top",
      "headline": "...",   // resolved to requested locale
      "subhead": "...",
      "asset_id": "<uuid>",
      "cta_kind": "category", "cta_target": "<uuid>",
      "cta_health": "verified",
      "scheduled_start_utc": "...", "scheduled_end_utc": "...",
      "priority_within_slot": 100,
      "market_code": "KSA"  // or '*'
    }
  ],
  "page": 1, "page_size": 50, "total_count": 5
}
```

**Sort**: per §R8 — specific-market first, then `*`; within tier, `priority_within_slot ASC` then `created_at_utc ASC`. CTA-broken banners filtered out (FR-022a); `cta_health=transient_unverified` banners INCLUDED.

**Errors**: `400 cms.storefront.market_unsupported`; `400 cms.storefront.locale_unsupported`; `429 cms.storefront.rate_limited`.

### §7.2 `GET /v1/storefront/cms/featured-sections`

Same envelope. Each item carries the live-resolved `references` plus `total_references`, `total_resolved`, `total_unavailable`, `omitted_due_to_unavailable_references`. Per FR-019.

### §7.3 `GET /v1/storefront/cms/faq`

**Query params**: `market`, `locale`, `category?`. Sort `display_order ASC` then `created_at_utc ASC`.

### §7.4 `GET /v1/storefront/cms/blog-articles`

**Query params**: `market`, `locale`, `category?`. Sort `published_at_utc DESC`. Single-locale articles return with `available_locales=['ar']` + `localization_unavailable_for_requested_locale=true` when the request `locale` doesn't match `authored_locale`.

### §7.5 `GET /v1/storefront/cms/blog-articles/{slug}`

**Query params**: `market`, `locale`. Returns full body + SEO block.

**Errors**: `404 cms.blog_article.not_found`.

### §7.6 `GET /v1/storefront/cms/legal-pages/{kind}`

**Query params**: `market`, `locale`. `kind` ∈ {terms, privacy, returns, cookies}. Single-row substitution per §R8: specific market → `*` → 404.

**200 Response**: the current `live` `LegalPageVersion` row.

**Errors**: `404 cms.legal_page.not_found_for_market`.

---

## §8. Preview endpoint (1)

### §8.1 `GET /v1/storefront/cms/preview/{kind}/{id}`

**Query params**: `token` (required, signed opaque string).

**Headers on response**: `X-Robots-Tag: noindex, nofollow` (FR-015).

**200 Response**: full draft entity row including a `preview_banner_marker` field for the storefront layer to render the "DRAFT — not for production" banner.

**Errors**:
- `403 cms.preview.token_signature_invalid` — HMAC mismatch (rotated secret or tampered token).
- `403 cms.preview.token_expired_or_revoked` — `expires_at_utc < now()` OR `revoked_at_utc` non-null.
- `404 cms.preview.entity_not_found` — entity_id resolves to nothing.
- `429 cms.preview.rate_limited` (60/min/IP).

---

## §9. Metrics endpoint (1)

### §9.1 `GET /v1/admin/cms/metrics`

**Permission**: `super_admin` or `cms.viewer.finance`.

**200 Response**:
```json
{
  "per_kind": {
    "banner_slot":      {"draft": 5, "scheduled": 2, "live": 12, "archived": 8, "stale_drafts": 1, "ownership_orphaned": 0, "cta_broken": 2},
    "featured_section": {"draft": 1, "scheduled": 0, "live": 6,  "archived": 3, "stale_drafts": 0, "ownership_orphaned": 0, "broken_refs_partial": 1, "broken_refs_full": 0},
    "faq_entry":        {"draft": 0, "scheduled": 0, "live": 60, "archived": 12, "stale_drafts": 0, "ownership_orphaned": 0},
    "blog_article":     {"draft": 4, "scheduled": 1, "live": 22, "archived": 5,  "stale_drafts": 2, "ownership_orphaned": 1},
    "legal_page_version": {"draft": 0, "scheduled": 1, "live": 8, "superseded": 11, "stale_drafts": 0}
  },
  "preview_tokens": {"active": 14, "expired_pending_cleanup": 6},
  "assets":         {"active": 240, "swept": 18, "in_grace_window": 5}
}
```

---

## §10. Reason-code inventory (43 owned codes)

| Code | HTTP | Where |
|---|---|---|
| `cms.editor.role_required` | 403 | every Editor endpoint |
| `cms.publish.role_required` | 403 | publish-now / schedule-publish |
| `cms.legal_page.publish.role_required` | 403 | legal page publish |
| `cms.legal_page.cross_market_requires_super_admin` | 403 | `*`-scoped legal page publish |
| `cms.publish.locale_completeness_missing` | 400 | per FR-007 |
| `cms.publish.effective_at_required` | 400 | legal page publish |
| `cms.banner.schedule_window_invalid` | 400 | banner save |
| `cms.banner.cta_kind_target_mismatch` | 400 | banner save |
| `cms.banner.external_url_https_required` | 400 | banner save |
| `cms.banner.cta_target_unresolvable` | 400 | banner publish |
| `cms.banner.slot_capacity_exceeded` | 400 | banner publish |
| `cms.banner.campaign_already_bound` | 400 | bind-campaign |
| `cms.banner.archive_blocked_by_campaign_binding` | 409 | banner archive |
| `cms.featured_section.empty_references` | 400 | featured save / publish |
| `cms.featured_section.too_many_references` | 400 | featured save |
| `cms.featured_section.reference_kind_unsupported` | 400 | featured save |
| `cms.faq.reorder_conflict` | 409 | bulk reorder |
| `cms.blog.slug_collision` | 400 | blog save |
| `cms.blog.slug_invalid_pattern` | 400 | blog save |
| `cms.blog.body_too_long` | 400 | blog save |
| `cms.asset.mime_forbidden` | 400 | banner / blog save |
| `cms.draft.not_editable` | 400 | PATCH on non-draft |
| `cms.draft.version_conflict` | 409 | xmin race |
| `cms.draft.delete_not_owner` | 403 | delete draft |
| `cms.archive.reason_note_required` | 400 | archive |
| `cms.{kind}.archive_forbidden_in_state` | 405 | archive |
| `cms.{kind}.delete_forbidden` | 405 | DELETE on non-draft (FR-005a) |
| `cms.legal_page.version.delete_forbidden` | 405 | DELETE on legal page version |
| `cms.legal_page.version_conflict` | 409 | concurrent legal page publish |
| `cms.legal_page.scope_not_cross_market` | 400 | publish-cross-market on non-`*` |
| `cms.legal_page.not_found_for_market` | 404 | storefront legal page read |
| `cms.preview.ttl_out_of_range` | 400 | mint preview token |
| `cms.preview.entity_not_draftable` | 400 | mint preview token |
| `cms.preview.token_signature_invalid` | 403 | preview read |
| `cms.preview.token_expired_or_revoked` | 403 | preview read |
| `cms.preview.token_not_found` | 404 | revoke preview token |
| `cms.preview.token_already_revoked` | 409 | revoke preview token (idempotent re-call returns 200) |
| `cms.preview.entity_not_found` | 404 | preview read |
| `cms.preview.rate_limited` | 429 | preview read |
| `cms.market_schema.value_out_of_range` | 400 | edit market schema |
| `cms.market_schema.version_conflict` | 409 | edit market schema |
| `cms.storefront.market_unsupported` | 400 | every storefront read |
| `cms.storefront.locale_unsupported` | 400 | every storefront read |
| `cms.storefront.rate_limited` | 429 | every storefront read |
| `cms.idempotency_key_conflict` | 409 | every state-transitioning POST (per spec 003 middleware) |
| `cms.admin_rate_limit_exceeded` | 429 | every admin POST (FR-032) |

(43 unique codes; the `cms.{kind}.archive_forbidden_in_state` and `cms.{kind}.delete_forbidden` count as one each per kind in OpenAPI but are listed as parameterised here.)

---

## §11. Domain events on the in-process MediatR bus (21)

All declared in `Modules/Shared/CmsContentDomainEvents.cs`. Each carries the standard envelope `{event_id, occurred_at_utc, actor_id?, entity_kind, entity_id, version_id, market_code, locale?, payload}`. Subscribers listed in `data-model.md §6`.

(See data-model.md §6 for the full list.)

---

## §12. Cross-module interfaces consumed (4)

| Interface | Direction | Provenance |
|---|---|---|
| `ICatalogProductReadContract` | 024 calls | spec 005 (or declared by 024 if 005 not yet at DoD) |
| `ICatalogCategoryReadContract` | 024 calls | same |
| `ICatalogBundleReadContract` | 024 calls | same |
| `ICustomerRoleLifecycleSubscriber.OnRoleRevoked(...)` | 024 subscribes | spec 004 |
| `ICampaignDeactivationSubscriber.OnCampaignDeactivated(...)` | 024 subscribes | spec 007-b |

(See data-model.md §7 for representative shape.)

---

## §13. Versioning

The `/v1/...` URL prefix is the contract version. Breaking changes to any endpoint shape will land under `/v2/...`; non-breaking additive changes (new optional fields, new `entity_kind`, new sort keys) ship under `/v1` with OpenAPI revisions.
