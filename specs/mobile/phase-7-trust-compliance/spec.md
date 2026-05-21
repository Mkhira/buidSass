# Spec — Phase 7: Customer Mobile Trust & Compliance (Verification + Reviews)

> **Phase:** 7 of 8 · **Owner:** mobile + verification + reviews · **Last updated:** 2026-05-19
> **OpenAPI sources:** [`openapi.verification.json`](../../../services/backend_api/openapi.verification.json), [`openapi.reviews.json`](../../../services/backend_api/openapi.reviews.json)
> **Endpoint count:** 8 verification customer + 6 customer reviews = 14.
> **Depends on:** Phase 1 (foundation), Phase 5 (verified-buyer gate references order state).

---

## 1. Goal

Deliver verification (KYC-style) submission, document upload, resubmit, renew, plus customer reviews (submit, edit, list, report). Restricted products in Phase 2 funnel users into the verification submit screen; eligible orders unlock the review submission CTA.

## 2. User roles

| Role | Phase 7 scope |
|---|---|
| Authenticated customer | All screens. |
| Restricted-product user | Verification submit → admin review → eligibility unlocked. |
| Verified buyer | Review submit/edit on products they purchased. |

## 3. Business rules

| BR | Rule | Reference |
|---|---|---|
| BR-1 | Verification submit schema is per-market dynamic — UI renders fields from `GET /api/customer/verifications/schema`. Never hardcoded. | Principles 5, 23 |
| BR-2 | Verification submit requires `Idempotency-Key`. | Principle 13 |
| BR-3 | Document upload happens after verification creation (the case must exist to attach to). One upload per call; multiple docs supported. | Principle 17 (analogue) |
| BR-4 | Resubmit endpoint is used after admin requested info; the screen surfaces a checklist of fixes derived from `verification.detail.requestedInfo[]`. | Principle 23 |
| BR-4a | Resubmit requires `Idempotency-Key` (one key per resubmit intent, regenerated each time the user re-enters the resubmit screen). | Principle 13 |
| BR-5 | Renewal endpoint creates a fresh case linked to a prior verification; UI surfaces both as separate cases in the list. | Principle 23 |
| BR-5a | Renew requires `Idempotency-Key` (one key per renew intent). | Principle 13 |
| BR-6 | Review submission requires verified-buyer eligibility (server enforces; client surfaces a "Buy this product to review it" empty state otherwise). | Principle 15 |
| BR-7 | Review submission carries `Idempotency-Key`. | Principle 13 |
| BR-8 | Reviews are single-locale per Principle 4's long-form rule. Submit screen offers a locale selector defaulting to the user's locale. | Principle 4, 15 |
| BR-9 | Report-review reasons come from `GET /v1/customer/reviews/report-reasons` (per-market enum). | Principle 15 |
| BR-10 | Review edits are gated by the server-supplied `editableUntil` timestamp on the review detail response. | Principle 15 |

## 4. Screens

### S-7.1 Verification list

**Status:** Done
**Route:** `/verification` · **Bottom nav:** visible (More tab)
**OpenAPI source:** `openapi.verification.json`
**Wireframe:** [`#phase-7-verification-list`](../../../docs/mobile-screens-wireframes.md#phase-7-verification-list--s-71-verification-list)

#### Endpoints used

| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /api/customer/verifications | mount + pull-to-refresh | safe | |
| GET | /api/customer/verifications/active | mount | safe | banner data — current active case |

#### Response data shape
```json
// list
{
  "items": [
    { "id": "uuid", "kind": "string", "state": "submitted | info_requested | approved | rejected | expired", "createdAt": "iso8601", "expiresAt": "iso8601?" }
  ]
}

// active
{
  "id": "uuid?",
  "kind": "string?",
  "state": "approved | submitted | info_requested | none",
  "expiresAt": "iso8601?"
}
```

#### UI states
loading skeleton → loaded (active banner + history list) → empty (no history, no active) → error/offline.

#### Bloc scaffold
- `VerificationListBloc`. Events: started, refreshed. States standard.

#### Acceptance criteria
- [x] Active banner styles by state (green=approved, amber=info_requested, grey=expired).
- [x] Start New CTA routes to S-7.2 with verification kind picker (or single kind based on user's needs).
- [x] Resume CTA routes to S-7.3 detail when an in-progress case exists.
- [x] AR + EN.
- [x] Tests.

---

### S-7.2 Submit verification

**Status:** Done
**Route:** `/verification/new` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.verification.json`
**Wireframe:** [`#phase-7-verification-submit`](../../../docs/mobile-screens-wireframes.md#phase-7-verification-submit--s-72-submit-verification)

#### Endpoints used

| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /api/customer/verifications/schema | mount | safe | per-market dynamic form |
| POST | /api/customer/verifications | submit | **Idempotency-Key required** | terminal write |

#### Response data shape
```json
// schema
{
  "kind": "string",
  "fields": [
    { "key": "businessLicense", "label": "string", "type": "text | number | enum | date | doc", "required": true, "options": ["..."], "validation": { "regex": "..." } }
  ],
  "documentSlots": [{ "key": "id_front", "label": "string", "required": true }]
}

// submit
{ "id": "uuid", "state": "submitted", "createdAt": "iso8601" }
```

#### UI states
loading-schema → form rendered dynamically from schema → submitting → submitted (routes to S-7.3 detail) → validation 422 (per-field) → error-5xx / offline.

#### Bloc scaffold
- `VerificationSubmitBloc`. Holds `Map<String, dynamic>` of field values; renders typed widgets per `fields[i].type`.
- Events: started(kind), fieldChanged(key, value), submitted.
- States: schemaLoading, form(schema, values, errors?), submitting, submitted(id), failure.

#### Acceptance criteria
- [x] Form fields rendered from schema (text, number, enum dropdown, date picker, document slot).
- [x] Required-field validation client-side.
- [x] Documents NOT uploaded here — only after submit (S-7.3).
- [x] Editorial AR copy.
- [x] Tests.

---

### S-7.3 Verification detail + document upload

**Status:** Done
**Route:** `/verification/{id}` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.verification.json`
**Wireframe:** [`#phase-7-verification-detail`](../../../docs/mobile-screens-wireframes.md#phase-7-verification-detail--s-73-verification-detail--docs-upload)

#### Endpoints used

| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /api/customer/verifications/{id} | mount + pull-to-refresh | safe | |
| POST | /api/customer/verifications/{id}/documents | per document upload | yes | multipart |

#### Response data shape
```json
// detail
{
  "id": "uuid",
  "state": "submitted | info_requested | approved | rejected | expired",
  "kind": "string",
  "createdAt": "iso8601",
  "fields": { "businessLicense": "AB123" },
  "documents": [{ "slotKey": "id_front", "url": "https://...", "uploadedAt": "iso8601" }],
  "requestedInfo": [{ "kind": "doc | field", "key": "id_front", "note": "string" }],
  "timeline": [{ "kind": "submitted | info_requested | approved | rejected", "occurredAt": "iso8601", "actor": "customer | admin", "note": "string?" }]
}
```

#### UI states
loading → loaded (timeline + fields + documents + requested-info checklist + Upload + Resubmit CTAs) → uploading-doc → upload-failed → error/offline.

#### Bloc scaffold
- `VerificationDetailBloc`. Events: started(id), refreshed, documentUploadRequested(slotKey, file). States: loading, loaded(detail), uploading(slotKey), failure.

#### Acceptance criteria
- [x] Document upload UI surfaces per-slot progress.
- [x] Multi-document upload runs in parallel with bounded concurrency (max 2 simultaneous).
- [x] On `info_requested`: requested-info checklist shown prominently; Resubmit CTA enabled once all requested items are addressed.
- [x] AR + EN.
- [x] Tests.

---

### S-7.4 Resubmit / Renew

**Status:** Done
**Route:** `/verification/{id}/resubmit` and `/verification/renew` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.verification.json`
**Wireframe:** [`#phase-7-verification-resubmit`](../../../docs/mobile-screens-wireframes.md#phase-7-verification-resubmit--s-74-resubmit--renew)

#### Endpoints used

| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /api/customer/verifications/{id}/resubmit | submit (resubmit flow) | yes | **Idempotency-Key required** (BR-4a) |
| POST | /api/customer/verifications/renew | submit (renew flow) | yes | **Idempotency-Key required** (BR-5a); creates a new linked case |

#### Response data shape
Resubmit → refreshed detail; renew → new case id + state submitted.

#### UI states
checklist (resubmit) or new form (renew) → submitting → success (routes back to detail or new detail) → 422 / 5xx / offline.

#### Bloc scaffold
Two cubits: `ResubmitCubit(verificationId)` and `RenewBloc`. Both standard.

#### Acceptance criteria
- [x] Resubmit: only requested-info items shown for editing; everything else read-only.
- [x] Renew: similar to S-7.2 with the prior case's data pre-filled.
- [x] AR + EN.
- [x] Tests.

---

### S-7.5 Submit review

**Status:** Done
**Route:** `/reviews/new?productId={id}&orderId={id}` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.reviews.json`
**Wireframe:** [`#phase-7-review-submit`](../../../docs/mobile-screens-wireframes.md#phase-7-review-submit--s-75-submit-review)

#### Endpoints used

| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /v1/customer/reviews | submit | **Idempotency-Key required** | verified-buyer gate (server) |

#### Response data shape
```json
{ "id": "uuid", "state": "pending_moderation | visible", "createdAt": "iso8601" }
```

#### UI states
form (stars + comment + optional media) → submitting → submitted (toast + back to PDP or My Reviews) → 403 (not eligible) → 5xx / offline.

#### Bloc scaffold
- `ReviewSubmitBloc`. Events: started(productId, orderId), starsChanged, commentChanged, mediaAdded, submitted. States: form, submitting, submitted, failure.

#### Acceptance criteria
- [x] Star rating 1–5 required.
- [x] Comment ≤ 2000 chars.
- [x] Media upload reuses pattern from Phase 6 photos (separate `/reviews/media` endpoint if backend offers, else inline).
- [x] 403 surfaces friendly "Only verified buyers can review" with link to orders.
- [x] AR + EN.
- [x] Tests.

---

### S-7.6 My reviews list

**Status:** Done
**Route:** `/my-reviews` · **Bottom nav:** visible (More)
**OpenAPI source:** `openapi.reviews.json`
**Wireframe:** [`#phase-7-my-reviews`](../../../docs/mobile-screens-wireframes.md#phase-7-my-reviews--s-76-my-reviews-list)

#### Endpoints used

| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /v1/customer/reviews/me | mount + filter + pagination | safe | |

#### Response data shape
```json
{
  "items": [
    {
      "id": "uuid",
      "productId": "uuid",
      "productName": "string",
      "rating": 5,
      "state": "pending_moderation | visible | flagged | hidden",
      "createdAt": "iso8601"
    }
  ],
  "page": 1, "pageSize": 20, "totalCount": 3
}
```

#### UI states
standard list states.

#### Bloc scaffold
`MyReviewsBloc` standard.

#### Acceptance criteria
- [x] State chips per row.
- [x] Tap row → S-7.7 detail.
- [x] Tests.

---

### S-7.7 My review detail + edit

**Status:** Done
**Route:** `/my-reviews/{id}` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.reviews.json`
**Wireframe:** [`#phase-7-review-detail`](../../../docs/mobile-screens-wireframes.md#phase-7-review-detail--s-77-my-review-detail--edit)

#### Endpoints used

| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /v1/customer/reviews/me/{id} | mount | safe | |
| PATCH | /v1/customer/reviews/{id} | Edit submit | yes | gated by `editableUntil` |

#### Response data shape
```json
{
  "id": "uuid",
  "productId": "uuid",
  "productName": "string",
  "rating": 5,
  "comment": "string",
  "media": [{ "url": "..." }],
  "state": "pending_moderation | visible | flagged | hidden",
  "createdAt": "iso8601",
  "editableUntil": "iso8601?",
  "moderationNote": "string?"
}
```

#### UI states
detail view → Edit toggles in-place form → save → success → moderation status shown.

#### Bloc scaffold
`MyReviewDetailBloc` with Events started, editToggled, fieldChanged, saved. States loaded(detail, editing?), saving, failure.

#### Acceptance criteria
- [x] Edit CTA disabled when `editableUntil` is past.
- [x] Moderation status visible (e.g., "Hidden by moderation").
- [x] AR + EN.
- [x] Tests.

---

### S-7.8 Report review (other users' reviews)

**Status:** Done
**Route:** `/reviews/{id}/report` (or modal from PDP review list)
**OpenAPI source:** `openapi.reviews.json`
**Wireframe:** [`#phase-7-report`](../../../docs/mobile-screens-wireframes.md#phase-7-report--s-78-report-review)

#### Endpoints used

| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /v1/customer/reviews/report-reasons | mount | safe | per-market |
| POST | /v1/customer/reviews/{id}/report | submit | yes | |

#### Response data shape
```json
// reasons
[{ "key": "spam | abuse | fake | other", "label": "string" }]

// report
{ "id": "uuid", "state": "submitted" }
```

#### UI states
reasons load → radio + note → submit → toast "Thanks — we'll review" → error/offline.

#### Bloc scaffold
`ReportReviewBloc` standard.

#### Acceptance criteria
- [x] Reasons from server (BR-9).
- [x] Note optional.
- [x] Tests.

---

## 5. Acceptance criteria — phase-wide

- [x] 8 screens above pass per-screen DoD.
- [x] Verification form rendered dynamically from server schema (BR-1).
- [x] Verification submit + review submit both use Idempotency-Key.
- [x] Review edit gated by `editableUntil`.
- [x] Report reasons from server.
- [x] `flutter analyze` + `flutter test` green.
- [x] §8 row → **Done**.

## 6. Dependencies

- Phase 1 (foundation, More hub placeholders).
- Phase 5 (order detail → review CTA per delivered order).
- Phase 2 (PDP → review aggregates already wired; review list on PDP is out of Phase 7 scope unless required — see "Out of scope").

## 7. Out of scope

- Review list on PDP (anonymous browsing of all reviews per product) — not in OpenAPI surface; deferred.
- Reviewer profile pages — not in launch.
- Review helpfulness votes — not in OpenAPI surface; deferred.

## 8. References

- Principles 4, 5, 13, 15, 23, 24, 27, 28.
