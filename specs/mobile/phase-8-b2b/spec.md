# Spec — Phase 8: Customer Mobile B2B (Quotes + Companies)

> **Phase:** 8 of 8 · **Owner:** mobile + b2b · **Last updated:** 2026-05-19
> **OpenAPI sources:** [`openapi.b2b.json`](../../../services/backend_api/openapi.b2b.json), [`openapi.orders.json`](../../../services/backend_api/openapi.orders.json) (legacy quotations).
> **Endpoint count:** 20 b2b + 4 legacy quotations = 24.
> **Depends on:** Phase 1 (foundation), Phase 4 (quote-from-cart entry), Phase 5 (legacy quotations co-located).

---

## 1. Goal

Deliver the full B2B customer surface: quote requests (from cart and from product), quote list / detail / actions / documents, plus company management (profile, branches, invitations, memberships). Legacy `quotations` endpoints (predecessor to the b2b quote model) are surfaced here as a separate read-only flow with accept/reject actions.

After Phase 8, a B2B buyer can request a quote, approve one as an approver, finalize acceptance, and download the quote document. Companies can be registered, configured, branched, and have members invited/managed.

## 2. User roles

| Role | Phase 8 scope |
|---|---|
| Authenticated B2B buyer | Create + view + act on own quotes; download documents. |
| Authenticated B2B approver | See "Awaiting my approval" list; submit/finalize/reject acceptance. |
| Company admin | Register company; manage branches; invite + manage members. |
| Invitee (deep link) | Accept or decline an invitation via deep link. |
| Authenticated customer | Legacy quotations accept/reject (some accounts may have legacy quotes in flight at migration). |

## 3. Business rules

| BR | Rule | Reference |
|---|---|---|
| BR-1 | Quote creation endpoints require `Idempotency-Key`. | Principle 13 |
| BR-2 | Multi-step quote acceptance (submit → finalize) gives a two-eyes safety on B2B finalization; UI surfaces the current acceptance step clearly. | Principle 9 |
| BR-3 | Quote documents are binary; downloaded via the same caching strategy as Phase 6 invoices. Filename localized per `locale` path parameter. | Principle 18 (analogue) |
| BR-4 | Company config edits (`PATCH /companies/{id}`) require an admin role on that company; UI shows read-only mode for non-admins. | Principle 9 |
| BR-5 | Invitation accept/decline lives under `/companies/invitations/{token}/...` (token-bound); deep link from email/SMS opens the app to S-8.11. | Principle 12 (analogue) |
| BR-6 | Memberships PATCH (role change) and DELETE (remove member) require admin role. UI hides actions otherwise. | Principle 9 |
| BR-7 | "Save as template" stores the quote as a repeat-order template; this is a write-only call from mobile in this phase (read of templates is out of scope). | Principle 9 |
| BR-8 | Legacy quotations (S-8.legacy.1/2) are surfaced as a read-only flow with Accept/Reject actions. Server-side may eventually deprecate; UI gracefully handles 404 if a customer has no legacy quotes. | Principle 17 |

## 4. Screens

### S-8.1 My quotes list

**Status:** Done
**Route:** `/quotes` · **Bottom nav:** visible (More → Company → Quotes)
**OpenAPI source:** `openapi.b2b.json`
**Wireframe:** [`#phase-8-quotes-list`](../../../docs/mobile-screens-wireframes.md#phase-8-quotes-list--s-81-my-quotes)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /api/customer/quotes | mount + filter + pagination | safe | |

#### Response data shape
```json
{
  "items": [
    {
      "id": "uuid",
      "quoteNumber": "Q-2026-05-000045",
      "state": "draft | published | awaiting_acceptance | accepted | rejected | withdrawn | expired",
      "totals": { "amount": "1500.00", "currency": "SAR" },
      "createdAt": "iso8601",
      "expiresAt": "iso8601?"
    }
  ],
  "page": 1, "pageSize": 20, "totalCount": 6
}
```

#### UI states
loading → list with filter chips → empty → error/offline.

#### Bloc scaffold
`MyQuotesBloc` standard.

#### Acceptance criteria
- [x] Filter chips reflect server enum.
- [x] State pill per row.
- [x] AR + EN.
- [x] Tests.

---

### S-8.2 Awaiting my approval

**Status:** Done
**Route:** `/quotes/awaiting-approval` · **Bottom nav:** visible (Approver role)
**OpenAPI source:** `openapi.b2b.json`
**Wireframe:** [`#phase-8-quotes-awaiting`](../../../docs/mobile-screens-wireframes.md#phase-8-quotes-awaiting--s-82-awaiting-my-approval)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /api/customer/quotes/awaiting-my-approval | mount + refresh | safe | |

#### Response
Same shape as S-8.1 items, possibly with `submittedAt` + submitter info.

#### Acceptance criteria
- [x] Hidden from buyers who aren't approvers (gate via `me.roles`).
- [x] Tap routes to quote detail (S-8.5).
- [x] Tests.

---

### S-8.3 Request quote from cart

**Status:** Done
**Route:** `/quotes/from-cart` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.b2b.json`
**Wireframe:** [`#phase-8-quote-from-cart`](../../../docs/mobile-screens-wireframes.md#phase-8-quote-from-cart--s-83-quote-from-cart)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /api/customer/quotes/from-cart | submit | **Idempotency-Key required** | terminal write |

#### Response
```json
{ "id": "uuid", "quoteNumber": "string", "state": "draft", "createdAt": "iso8601" }
```

#### UI states
form (cart summary read-only + terms field + expected delivery + note) → submitting → success routes to S-8.5 → 422 / 5xx / offline.

#### Bloc scaffold
`QuoteFromCartBloc` — events: started(cartSnapshot), termsChanged, etaChanged, noteChanged, submitted. States: form, submitting, submitted(quoteId), failure.

#### Acceptance criteria
- [x] Submit reuses cart contents at the moment of entry (no mutation).
- [x] Idempotency-Key generated once on entry.
- [x] AR + EN.
- [x] Tests.

---

### S-8.4 Request quote from product

**Status:** Done
**Route:** `/products/{slug}/quote` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.b2b.json`
**Wireframe:** [`#phase-8-quote-from-product`](../../../docs/mobile-screens-wireframes.md#phase-8-quote-from-product--s-84-quote-from-product)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /api/customer/quotes/from-product | submit | **Idempotency-Key required** | terminal write |

#### Response
Same as S-8.3.

#### UI states
form (product snapshot + qty + terms + note) → submitting → success routes to S-8.5 → standard errors.

#### Bloc scaffold
`QuoteFromProductBloc` standard.

#### Acceptance criteria
- [x] CTA visible from PDP (Phase 2) under "Request Quote" for B2B accounts.
- [x] Tests.

---

### S-8.5 Quote detail + actions

**Status:** Done
**Route:** `/quotes/{id}` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.b2b.json`
**Wireframe:** [`#phase-8-quote-detail`](../../../docs/mobile-screens-wireframes.md#phase-8-quote-detail--s-85-quote-detail--actions)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /api/customer/quotes/{id} | mount + pull-to-refresh | safe | |
| POST | /api/customer/quotes/{id}/submit-acceptance | action | yes | step 1 of 2 |
| POST | /api/customer/quotes/{id}/finalize-acceptance | action | yes | step 2 of 2 |
| POST | /api/customer/quotes/{id}/reject-acceptance | action | yes | terminal |
| POST | /api/customer/quotes/{id}/request-revision | action | yes | |
| POST | /api/customer/quotes/{id}/withdraw | action | yes | terminal |
| POST | /api/customer/quotes/{id}/save-as-template | action | yes | |

#### Response data shape
```json
{
  "id": "uuid",
  "quoteNumber": "Q-...",
  "state": "draft | published | awaiting_acceptance | accepted | rejected | withdrawn | expired",
  "versions": [
    {
      "versionId": "uuid",
      "publishedAt": "iso8601",
      "lines": [{ "productId": "uuid", "name": "string", "qty": 1, "unitPrice": "string", "lineTotal": "string" }],
      "totals": { "subtotal": "string", "discount": "string", "tax": "string", "grandTotal": "string", "currency": "SAR | EGP" },
      "terms": "string",
      "validUntil": "iso8601",
      "documents": [{ "locale": "ar | en", "url": "..." }]
    }
  ],
  "actions": {
    "canSubmitAcceptance": true,
    "canFinalizeAcceptance": false,
    "canRejectAcceptance": true,
    "canRequestRevision": true,
    "canWithdraw": true,
    "canSaveAsTemplate": true
  },
  "submittedBy": { "userId": "uuid", "name": "string", "submittedAt": "iso8601?" }
}
```

#### UI states
loading → loaded (version timeline + pricing table + terms + actions toolbar) → action-submitting (per CTA) → action-success (refresh) → 409 (state changed elsewhere) → 5xx / offline.

#### Bloc scaffold
- `QuoteDetailBloc`.
- Events: `QuoteStarted(id)`, `QuoteRefreshed`, `QuoteActionRequested(kind)`.
- States: `QuoteLoading`, `QuoteLoaded(quote)`, `QuoteActing(kind)`, `QuoteActionResult(kind, success)`, `QuoteFailure(reason, correlationId)`.

#### Acceptance criteria
- [x] Action buttons gated by `actions.*` (BR-2 enforced server-side; UI mirrors).
- [x] Version timeline shows all versions with publish dates.
- [x] Submit-then-finalize flow is clear in the UI (current step badge).
- [x] On any action 409 → refresh detail silently and re-render gating.
- [x] AR + EN.
- [x] Tests.

---

### S-8.6 Quote document download

**Status:** Done
**Route:** triggered from S-8.5 (no dedicated route)
**OpenAPI source:** `openapi.b2b.json`
**Wireframe:** [`#phase-8-quote-document`](../../../docs/mobile-screens-wireframes.md#phase-8-quote-document--s-86-quote-document-download)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /api/customer/quotes/{quoteId}/versions/{versionId}/documents/{locale} | tap Download | safe | binary |

#### Response
Binary PDF.

#### UI states
downloading → ready (Open / Share) → 404 (no document for this locale yet) → 5xx / offline.

#### Bloc scaffold
`QuoteDocumentBloc` mirrors Phase 6 `InvoicePdfBloc` structure (download → temp cache → open/share).

#### Acceptance criteria
- [x] Locale picker between AR / EN; default to current locale.
- [x] Cache key `quoteId-versionId-locale`.
- [x] AR filename `عرض-سعر-{quoteNumber}-{versionId}.pdf`; EN `quote-{quoteNumber}-{versionId}.pdf`.
- [x] Tests.

---

### S-8.7 Company registration

**Status:** Done
**Route:** `/company/register` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.b2b.json`
**Wireframe:** [`#phase-8-company-register`](../../../docs/mobile-screens-wireframes.md#phase-8-company-register--s-87-company-registration)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /api/customer/companies | submit | **Idempotency-Key required** | terminal |

#### Response
```json
{ "id": "uuid", "name": "string", "createdAt": "iso8601" }
```

#### UI states
form (name + VAT + address + commercial registration number) → submitting → success routes to S-8.8 → 422 → 5xx / offline.

#### Bloc scaffold
`CompanyRegisterBloc` standard.

#### Acceptance criteria
- [x] VAT number validated per market regex (server confirms).
- [x] Idempotency-Key once on entry.
- [x] AR + EN.
- [x] Tests.

---

### S-8.8 Company profile

**Status:** Done
**Route:** `/company/{id}` · **Bottom nav:** visible (More → Company)
**OpenAPI source:** `openapi.b2b.json`
**Wireframe:** [`#phase-8-company-profile`](../../../docs/mobile-screens-wireframes.md#phase-8-company-profile--s-88-company-profile)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /api/customer/companies/{id} | mount | safe | |
| PATCH | /api/customer/companies/{id} | save (admin only) | yes | |

#### Response
```json
{
  "id": "uuid",
  "name": "string",
  "vatNumber": "string",
  "address": "string",
  "commercialRegistration": "string?",
  "marketCode": "SA | EG",
  "myRole": "admin | buyer | approver",
  "branches": [{ "id": "uuid", "name": "string", "address": "string" }],
  "memberships": [{ "id": "uuid", "userId": "uuid", "name": "string", "role": "admin | buyer | approver" }]
}
```

#### UI states
loading → loaded (read-only for non-admins; editable for admins) → saving → success → error/offline.

#### Bloc scaffold
`CompanyProfileBloc` events: started, refreshed, fieldChanged, saved. States: loading, loaded(company, editable?), saving, failure.

#### Acceptance criteria
- [x] Non-admins see all fields as read-only (BR-4).
- [x] Tabs/sections for Profile / Branches / Members.
- [x] AR + EN.
- [x] Tests.

---

### S-8.9 Branches

**Status:** Done
**Route:** `/company/{id}/branches` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.b2b.json`
**Wireframe:** [`#phase-8-branches`](../../../docs/mobile-screens-wireframes.md#phase-8-branches--s-89-branches)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /api/customer/companies/{id}/branches | Add Branch submit | yes | |
| DELETE | /api/customer/companies/{id}/branches/{branchId} | Delete tap | yes | |

#### UI states
list (from S-8.8 response) → add-form modal → submitting → success refresh → confirm-delete modal → success refresh → standard errors.

#### Bloc scaffold
`BranchesBloc` events: addRequested, addSubmitted, deleteRequested, deleteConfirmed. States: list, adding, deleting, failure.

#### Acceptance criteria
- [x] Admin only (BR-4).
- [x] Delete requires confirmation modal.
- [x] AR + EN.
- [x] Tests.

---

### S-8.10 Invite user

**Status:** Done
**Route:** `/company/{id}/invitations/new` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.b2b.json`
**Wireframe:** [`#phase-8-invite-user`](../../../docs/mobile-screens-wireframes.md#phase-8-invite-user--s-810-invite-user)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /api/customer/companies/{id}/invitations | submit | yes | sends email/SMS server-side |

#### UI states
form (email + role) → submitting → success toast → 422 → 5xx / offline.

#### Bloc scaffold
`InviteUserBloc` standard.

#### Acceptance criteria
- [x] Role enum from server.
- [x] Admin only.
- [x] Tests.

---

### S-8.11 Invitations (accept/decline deep link)

**Status:** Done
**Route:** `/invitations/{token}` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.b2b.json`
**Wireframe:** [`#phase-8-invitations`](../../../docs/mobile-screens-wireframes.md#phase-8-invitations--s-811-invitations-acceptdecline-deep-link)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /api/customer/companies/invitations/{token}/accept | Accept tap | yes | token-bound |
| POST | /api/customer/companies/invitations/{token}/decline | Decline tap | yes | token-bound |

#### UI states
loading-validation → loaded (company name + role + Accept / Decline) → success (route to company profile or home) → 410 (token expired) → 5xx / offline.

#### Bloc scaffold
`InvitationAcceptBloc` events: started(token), accepted, declined. States: validating, loaded(companyName, role), submitting, success(companyId | declined), failure.

#### Acceptance criteria
- [x] Deep-link entry works from cold start (waits on SessionStore).
- [x] Authenticated path required (BR — server enforces); anonymous user prompted to sign in then resumed.
- [x] AR + EN.
- [x] Tests.

---

### S-8.12 Memberships

**Status:** Done
**Route:** `/company/{id}/members` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.b2b.json`
**Wireframe:** [`#phase-8-memberships`](../../../docs/mobile-screens-wireframes.md#phase-8-memberships--s-812-memberships)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| PATCH | /api/customer/companies/{id}/memberships/{membershipId} | role change | yes | |
| DELETE | /api/customer/companies/{id}/memberships/{membershipId} | remove | yes | |

#### UI states
list (from S-8.8) → editing-role per row → saving → success refresh → confirm-remove modal → success refresh → 409 (already changed) → 5xx / offline.

#### Bloc scaffold
`MembershipsBloc` events: roleChanged, removeRequested, removeConfirmed. States: list, editing(membershipId), removing(membershipId), failure.

#### Acceptance criteria
- [x] Admin only (BR-6).
- [x] Cannot demote self below admin if last admin (server enforces; UI defensive).
- [x] Tests.

---

### S-8.legacy.1 Legacy quotations list

**Status:** Done
**Route:** `/legacy-quotations` · **Bottom nav:** visible (More → Legacy Quotations)
**OpenAPI source:** `openapi.orders.json`
**Wireframe:** [`#phase-8-legacy-quotations`](../../../docs/mobile-screens-wireframes.md#phase-8-legacy-quotations--s-8legacy12-legacy-quotations)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /v1/customer/quotations | mount | safe | |

#### Acceptance criteria
- [x] If empty (most modern accounts), screen is hidden from menu entirely.
- [x] Tests.

---

### S-8.legacy.2 Legacy quotation detail / accept / reject

**Status:** Done
**Route:** `/legacy-quotations/{id}`
**OpenAPI source:** `openapi.orders.json`
**Wireframe:** see S-8.legacy.1 wireframe (combined).

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /v1/customer/quotations/{id} | mount | safe | |
| POST | /v1/customer/quotations/{id}/accept | Accept tap | yes | |
| POST | /v1/customer/quotations/{id}/reject | Reject tap | yes | |

#### Acceptance criteria
- [x] Detail renders line items + totals + Accept/Reject toolbar.
- [x] Confirm modal on Accept.
- [x] Tests.

---

## 5. Acceptance criteria — phase-wide

- [x] 12 screens above + 2 legacy pass per-screen DoD.
- [x] All quote/company create endpoints carry Idempotency-Key.
- [x] Action gating reads from `actions.*` payload — not from state inspection (BR-2).
- [x] Company profile is read-only for non-admins.
- [x] Invitation deep link resumes from cold start.
- [x] Quote document download reuses Phase 6 PDF cache pattern.
- [x] `flutter analyze` + `flutter test` green.
- [x] §8 row → **Done**.

## 6. Dependencies

- Phase 1 (foundation, deep-link routing, More hub).
- Phase 4 (Quote from cart entry).
- Phase 2 (Quote from product entry from PDP).

## 7. Out of scope

- Repeat-order templates UI (save-as-template is fire-and-forget here).
- Approver workflows beyond submit/finalize (e.g., multi-step approval chains) — single-step model at launch.
- Bulk membership operations (CSV upload).

## 8. References

- Principles 4, 5, 9, 12, 13, 18, 24, 27, 28.
- ADR-002 (Bloc).
