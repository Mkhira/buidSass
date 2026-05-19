# Quickstart — Phase 7: Trust & Compliance

## Prerequisites

- Phase 1 + Phase 5 complete.
- Backend seeded with: a verification schema per market; at least one delivered order so a verified-buyer review can be submitted.

## Manual smoke (Phase 7 exit gate)

1. **Open verification list.** From More → Verification → empty state for fresh accounts.
2. **Submit verification.** Tap Start New → schema loads → fill required fields → submit → routes to detail.
3. **Upload documents.** From detail, tap each document slot → pick file → upload progress → success.
4. **Info requested.** Admin requests info (dev tool or admin UI) → reopen detail → requested-info checklist visible → Resubmit CTA active after addressing items.
5. **Resubmit.** Tap Resubmit → only requested-info fields editable → submit → state updates.
6. **Renew.** From an approved+near-expiry verification, tap Renew → prior data pre-filled → submit → new case appears in list.
7. **Submit a review.** From an order detail → Write Review CTA → stars + comment → submit → My Reviews shows new review with `pending_moderation`.
8. **Review edit window.** Edit within window → save succeeds. After window ends (dev tool), edit CTA disabled.
9. **Report a review.** From PDP review list (if surfaced) or from My Reviews of someone else (admin-only) → reasons load → submit → toast.
10. **Verified-buyer gate.** Try to review a product not on any delivered order → "Only verified buyers" empty state.

## Automated

```sh
flutter analyze
flutter test test/features/verification/
flutter test test/features/reviews/
```

## Troubleshooting

- **Schema fails to render:** check `SchemaField.type` switch covers all server types; fall back to text input for unknown types.
- **Document upload stalls:** verify multipart boundary handling in Dio + server max upload size.
- **Review 403 spurious:** verify the order's delivery state is reflected in the `me` claim or in the server's verified-buyer query.
