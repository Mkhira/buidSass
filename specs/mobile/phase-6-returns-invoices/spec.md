# Spec — Phase 6: Customer Mobile Returns & Invoices

> **Phase:** 6 of 8 · **Owner:** mobile + returns + invoices · **Last updated:** 2026-05-19
> **OpenAPI sources:** [`openapi.returns.json`](../../../services/backend_api/openapi.returns.json), [`openapi.invoices.json`](../../../services/backend_api/openapi.invoices.json), [`openapi.orders.json`](../../../services/backend_api/openapi.orders.json) (return-eligibility entry).
> **Endpoint count:** 4 returns + 2 invoices + 1 orders (return-eligibility, reused from Phase 5).
> **Depends on:** Phase 5 (order detail entry).

---

## 1. Goal

Deliver returns and invoice access for completed orders: list returns, request a return (line selection + reason + photos), view return detail with refund state, preview an invoice, and download/share the tax invoice PDF.

## 2. User roles

| Role | Phase 6 scope |
|---|---|
| Authenticated customer | All screens. |
| B2B buyer | Same + invoice carries B2B billing details (server-side rendered). |

## 3. Business rules

| BR | Rule | Reference |
|---|---|---|
| BR-1 | Return wizard requires `return-eligibility` to surface at least one eligible line. Entry from order detail (Phase 5) is gated by `anyEligible=true`. | Principle 17 |
| BR-2 | Photo upload happens BEFORE return creation: `/v1/customer/returns/photos` returns a photo id; multiple photos accepted; the create-return call references the ids. | Principle 17 |
| BR-3 | Return create endpoint requires `Idempotency-Key` (one key per user intent — generated on entry to the wizard). | Principle 13 |
| BR-4 | Refund state is reflected in Phase 5 order detail's refund pill; Phase 6 owns the request initiation and the per-return detail screen. | Principle 17 |
| BR-5 | Invoice download is binary; rendered via system viewer or shared via share-sheet. No in-app PDF reader required. | Principle 18 |
| BR-6 | Invoice availability depends on order state (typically `paymentState=captured`). Order detail surfaces an "Invoice" CTA only when available; Phase 6 invoice screens defend against 404 in case the user lands via a stale link. | Principle 18 |
| BR-7 | Per-market tax fields rendered as the server returns them (VAT 15% KSA, VAT 14% EG); UI never computes tax. | Principle 18 |

## 4. Screens

### S-6.1 Returns list

**Status:** Planned
**Route:** `/returns` · **Bottom nav:** visible (More tab → My Returns; also linked from order detail)
**OpenAPI source:** `openapi.returns.json`
**Wireframe:** [`#phase-6-returns-list`](../../../docs/mobile-screens-wireframes.md#phase-6-returns-list--s-61-returns-list)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /v1/customer/returns | mount + filter + pagination + pull-to-refresh | safe | filters: status, page, pageSize |

#### Response data shape
```json
{
  "items": [
    {
      "id": "uuid",
      "returnNumber": "R-2026-05-000045",
      "orderId": "uuid",
      "orderNumber": "2026-05-000123",
      "createdAt": "iso8601",
      "state": "pending | approved | received | inspected | issued | rejected",
      "refundAmount": { "amount": "120.00", "currency": "SAR" }
    }
  ],
  "page": 1, "pageSize": 20, "totalCount": 5
}
```

#### UI states
loading → list → empty ("No returns yet") → error/offline.

#### Bloc scaffold
- `ReturnsListBloc`. Standard.

#### Acceptance criteria
- [ ] Filter chips: All / Pending / Approved / Issued / Rejected.
- [ ] Row taps route to S-6.3 detail.
- [ ] AR + EN.
- [ ] Tests.

---

### S-6.2 Return wizard (eligibility + create)

**Status:** Planned
**Route:** `/orders/{orderId}/returns/new` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.returns.json` + `openapi.orders.json` (eligibility)
**Wireframe:** [`#phase-6-return-create`](../../../docs/mobile-screens-wireframes.md#phase-6-return-create--s-62b-return-create-wizard) (+ eligibility entry [`#phase-6-return-eligibility`](../../../docs/mobile-screens-wireframes.md#phase-6-return-eligibility--s-62a-return-eligibility-entry))

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /v1/customer/orders/{orderId}/return-eligibility | mount | safe | gates line selection |
| POST | /v1/customer/returns/photos | per photo (multipart) | yes | returns photoId |
| POST | /v1/customer/orders/{orderId}/returns | submit | **Idempotency-Key required** | terminal |

#### Response data shape
```json
// photos upload
{ "photoId": "uuid", "url": "https://...", "checksum": "string" }

// return creation
{
  "id": "uuid",
  "returnNumber": "R-2026-...",
  "state": "pending",
  "createdAt": "iso8601",
  "linesRequested": [{ "productId": "uuid", "qty": 1, "reason": "string" }]
}
```

#### UI states
| State | Trigger | What renders |
|---|---|---|
| loading | mount | spinner over eligibility check |
| eligible | 2xx with eligible lines | line selection + reason picker + photo upload area + Submit |
| ineligible | 2xx with anyEligible=false | empty state "No eligible lines for return" + Back |
| uploading-photo | photo POST in-flight | per-tile progress |
| photo-failed | upload error | retry CTA on the tile |
| submitting | create POST in-flight | full-screen spinner |
| submitted | 201 | route to S-6.3 detail |
| validation | 422 | per-field error |
| error-409 | 409 (eligibility expired) | banner + Refresh CTA |
| error-5xx | 5xx | retry banner |
| offline | network | offline badge; submit disabled |

#### Bloc scaffold
- `ReturnWizardBloc`. Generates Idempotency-Key on entry.
- Events: started(orderId), lineSelected(productId, qty), reasonChanged(productId, reason), photoAddRequested, photoCancelled(photoId), submitted.
- States: `WizardLoading`, `WizardForm(eligibility, selectedLines, photos, errors?)`, `WizardSubmitting`, `WizardSubmitted(returnId)`, `WizardFailure(reason, correlationId)`.

#### Acceptance criteria
- [ ] At least one eligible line must be selected to enable Submit.
- [ ] Reason picker comes from a server-supplied enum (fallback list documented if endpoint not present).
- [ ] Photo upload uses `Idempotency-Key` per upload (re-uploads same checksum yield same id).
- [ ] Final submit reuses one Idempotency-Key across retries.
- [ ] Submit-success routes to detail.
- [ ] AR + EN.
- [ ] Tests.

#### Edge cases
- Photo > 10 MB ⇒ client downscales before upload.
- Network drop mid-upload ⇒ resume button on the tile; checksum-based dedupe.

---

### S-6.3 Return detail

**Status:** Planned
**Route:** `/returns/{id}` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.returns.json`
**Wireframe:** [`#phase-6-return-detail`](../../../docs/mobile-screens-wireframes.md#phase-6-return-detail--s-63-return-detail)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /v1/customer/returns/{id} | mount + pull-to-refresh | safe | |

#### Response data shape
```json
{
  "id": "uuid",
  "returnNumber": "string",
  "orderId": "uuid",
  "orderNumber": "string",
  "state": "pending | approved | received | inspected | issued | rejected",
  "timeline": [
    { "kind": "created | approved | received | inspected | issued | rejected", "occurredAt": "iso8601", "actor": "customer | admin | system", "note": "string?" }
  ],
  "lines": [
    { "productId": "uuid", "name": "string", "qty": 1, "reason": "string", "photos": [{ "url": "https://..." }] }
  ],
  "refund": { "amount": "string", "currency": "string", "method": "string", "issuedAt": "iso8601?" },
  "rejection": { "reason": "string?", "noteToCustomer": "string?" }
}
```

#### UI states
loading → loaded (state pill + timeline + items + photos + refund + rejection if any) → error/offline.

#### Bloc scaffold
- `ReturnDetailBloc`. Standard.

#### Acceptance criteria
- [ ] Photos rendered as a gallery with zoom.
- [ ] Rejected returns show the rejection reason prominently.
- [ ] AR + EN.
- [ ] Tests.

---

### S-6.4 Invoice preview

**Status:** Planned
**Route:** `/orders/{orderId}/invoice` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.invoices.json`
**Wireframe:** [`#phase-6-invoice-preview`](../../../docs/mobile-screens-wireframes.md#phase-6-invoice-preview--s-64-invoice-preview)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /v1/customer/orders/{orderId}/invoice | mount | safe | JSON preview (line items + totals + tax fields) |

#### Response data shape
```json
{
  "invoiceNumber": "INV-2026-05-000123",
  "issuedAt": "iso8601",
  "currency": "SAR | EGP",
  "billing": { "name": "string", "address": "string", "vatNumber": "string?" },
  "lines": [{ "name": "string", "qty": 1, "unitPrice": "120.00", "taxRate": "0.15", "lineTotal": "138.00" }],
  "totals": { "subtotal": "...", "taxTotal": "...", "grandTotal": "..." },
  "downloadUrl": "/v1/customer/orders/{orderId}/invoice.pdf"
}
```

#### UI states
loading → loaded (preview + Download PDF CTA) → 404 (invoice not yet available — e.g., bank transfer not confirmed) shows empty state with "Available after payment is captured" → error/offline.

#### Bloc scaffold
- `InvoicePreviewBloc`. Standard.

#### Acceptance criteria
- [ ] AR locale formats numbers per locale; EN locale formats per locale.
- [ ] VAT rate explicitly shown.
- [ ] Download CTA navigates to S-6.5 (or directly triggers download).
- [ ] Tests.

---

### S-6.5 Invoice PDF download

**Status:** Planned
**Route:** `/orders/{orderId}/invoice/pdf` (or no route — trigger from S-6.4)
**OpenAPI source:** `openapi.invoices.json`
**Wireframe:** [`#phase-6-invoice-pdf`](../../../docs/mobile-screens-wireframes.md#phase-6-invoice-pdf--s-65-invoice-pdf-download)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /v1/customer/orders/{orderId}/invoice.pdf | Download CTA | safe | binary; `Content-Type: application/pdf` |

#### Response data shape
binary PDF.

#### UI states
| State | Trigger | What renders |
|---|---|---|
| downloading | request in-flight | progress |
| ready | 2xx + saved to local cache | Open / Share / Download Again CTAs |
| open | tap Open | system viewer (`open_filex` or platform equivalent) |
| share | tap Share | share-sheet (`share_plus`) |
| error-404 | invoice not generated yet | banner + Refresh |
| error-5xx | 5xx | retry banner |
| offline | network | offline badge |

#### Bloc scaffold
- `InvoicePdfBloc`. Events: downloadRequested, openTapped, shareTapped. States: loading, ready(localPath), failure.

#### Acceptance criteria
- [ ] PDF saved to app cache directory; not exposed via document picker beyond the share-sheet.
- [ ] Cache key by `orderId + invoiceNumber + issuedAt` so updated invoices re-download.
- [ ] AR filename localized: `فاتورة-{orderNumber}.pdf` in AR; `invoice-{orderNumber}.pdf` in EN.
- [ ] Tests.

#### Edge cases
- Disk full ⇒ surface "Not enough space" error.

---

## 5. Acceptance criteria — phase-wide

- [ ] 5 screens above pass per-screen DoD.
- [ ] Return wizard uses Idempotency-Key per create and per photo upload.
- [ ] Invoice download caches locally; share-sheet works.
- [ ] No client-side tax computation (BR-7).
- [ ] `flutter analyze` + `flutter test` green.
- [ ] §8 row → **Done**.

## 6. Dependencies

- Phase 5 (order detail entry point + return-eligibility query reused).
- Backend specs: 013 (returns), 012 (tax invoices), 011 (orders for eligibility).

## 7. Out of scope

- Partial refunds with itemized customer-side adjustment — server-driven only.
- Return label printing — out of launch.
- Invoice email resend from mobile — admin-only.

## 8. References

- Principles 13, 17, 18, 24, 25, 27, 28.
