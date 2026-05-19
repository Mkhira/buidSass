# Quickstart — Phase 6: Returns & Invoices

## Prerequisites

- Phases 4 and 5 complete.
- Backend seeded with a `delivered` order that is within the return window.

## Run

```sh
cd apps/customer_flutter
flutter run
```

## Manual smoke (Phase 6 exit gate)

1. **Returns list empty.** Open `/returns` from More → empty state.
2. **Start a return.** From a delivered order → Return CTA → wizard opens with eligible lines + reason picker + photo tile.
3. **Photo upload.** Add 2 photos → see per-tile progress → both reach Ready state.
4. **Submit.** Submit return → routes to detail; states reflect Pending.
5. **Resume on failure.** During upload, kill the network for one photo → see Failed state → retry succeeds without re-uploading the file unchanged (server dedupes by checksum + clientPhotoKey).
6. **Return detail.** Refresh detail → timeline updates as backend transitions.
7. **Invoice preview.** Open an order with `paymentState=captured` → tap Invoice CTA → preview shows line items + 15% VAT (KSA) or 14% VAT (EG).
8. **PDF download.** Tap Download → progress → Ready → Open opens system viewer; Share opens share-sheet.
9. **Invoice not available.** Open an order in bank-transfer pending → Invoice preview shows the "Available after payment captures" empty state.

## Automated

```sh
flutter analyze
flutter test test/features/returns/
flutter test test/features/invoices/
```

## Troubleshooting

- **Upload retries duplicate photos:** ensure `clientPhotoKey` is stable per tile + Idempotency-Key matches.
- **PDF Open fails:** verify `open_filex` registered properly on iOS (LSApplicationQueriesSchemes) and Android (FileProvider).
