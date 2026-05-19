# Implementation Plan — Phase 6: Returns & Invoices

## Module layout

```
apps/customer_flutter/lib/features/
├── returns/
│   ├── data/{returns_gateway,returns_gateway_impl}.dart
│   ├── bloc/{returns_list_bloc,return_wizard_bloc,return_detail_bloc}.dart
│   ├── screens/{returns_list_screen,return_wizard_screen,return_detail_screen}.dart
│   └── widgets/{photo_upload_tile.dart,line_picker.dart,return_state_pill.dart}
└── invoices/
    ├── data/{invoices_gateway,invoices_gateway_impl}.dart
    ├── bloc/{invoice_preview_bloc,invoice_pdf_bloc}.dart
    └── screens/{invoice_preview_screen,invoice_pdf_screen}.dart
```

## Routing additions

```
/returns                                          → ReturnsListScreen
/orders/{orderId}/returns/new                     → ReturnWizardScreen
/returns/{id}                                     → ReturnDetailScreen
/orders/{orderId}/invoice                         → InvoicePreviewScreen
/orders/{orderId}/invoice/pdf                     → triggers InvoicePdfBloc (no screen by default; route exists for deep-link)
```

## Photo upload strategy

`ReturnWizardBloc` orchestrates photo uploads as background tasks per tile:

```dart
sealed class PhotoTileState { /* uploading, ready, failed */ }
```

Each tile carries:
- Original file reference (for re-upload on retry).
- Server `photoId` once upload returns.
- A `clientPhotoKey` (UUID v4) — sent as both the HTTP `Idempotency-Key` header AND the multipart form field `clientPhotoKey`. The server dedupes by `(clientPhotoKey, checksum)` so a retry yields the same `photoId`. See [`./data-model.md`](./data-model.md#post-v1customerreturnsphotos) and [`./contracts/README.md`](./contracts/README.md) for the exact contract.

**Cache key for any client-side state keyed on the photo:** `clientPhotoKey` (not `photoId`, since `photoId` is only known after upload succeeds).

Downscale images > 10 MB locally with `image` package to ≤ 2 MB before upload.

## PDF caching

`InvoicePdfBloc` downloads via Dio with `responseType: ResponseType.bytes`, writes to `getTemporaryDirectory()/invoices/{orderId}-{invoiceNumber}.pdf`.

**Logical cache key:** `(orderId, invoiceNumber, issuedAt)`. The on-disk filename only embeds `orderId` and `invoiceNumber`; `issuedAt` is the eviction trigger. Eviction rules:
- If the preview's `issuedAt` changes, the cached file is overwritten on next download.
- If the preview's `invoiceNumber` changes (regenerated invoice), the old file is orphaned and removed by the 30-day sweeper; the new file lives under the new `invoiceNumber` filename.

`Open` uses `open_filex`; `Share` uses `share_plus`. Both reject if the file is no longer at the cached path (re-download triggered).

## Build sequence

1. Returns gateway + invoices gateway (T-6.1, T-6.2).
2. Returns list (T-6.3).
3. Return wizard with photo upload (T-6.4 — biggest task).
4. Return detail (T-6.5).
5. Invoice preview (T-6.6).
6. Invoice PDF download + share (T-6.7).
7. Tests + exit (T-6.8, T-6.9).

## Risks specific to Phase 6

| # | Risk | Mitigation |
|---|---|---|
| 1 | Photo upload + return create double-charge as separate intents. | One Idempotency-Key per upload (checksum-based); one separate key for the create call. |
| 2 | Large photos cause memory pressure on Android. | Downscale to ≤ 2 MB before sending. |
| 3 | PDF cache leaks disk space. | Cleanup job on app start: remove files > 30 days old. |
| 4 | Invoice may not be ready (bank transfer pending) ⇒ 404 confuses users. | Friendly empty state with "Available after payment captures". |

## Definition of Done

See `checklists/dod.md`.
