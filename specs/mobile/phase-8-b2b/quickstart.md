# Quickstart — Phase 8: B2B

## Prerequisites

- Phases 1–6 complete (Phase 7 not strictly required but recommended).
- Backend seeded with: a company for the test account; a published quote; a pending-approval quote where the test user is the approver; a legacy quotation row for an alternate test account (if testing the legacy flow).

## Manual smoke (Phase 8 exit gate)

1. **My quotes empty / non-empty.** Open `/quotes` → list renders.
2. **Awaiting approval.** Sign in as an approver → tab visible → list shows pending.
3. **Quote from cart.** From a populated cart → "Request Quote" CTA → quote-from-cart screen → submit → routes to quote detail.
4. **Quote from product.** From PDP → "Request Quote" CTA → submit → quote detail.
5. **Quote detail + actions.** Open a published quote → version timeline visible → submit-acceptance → state pill updates → finalize-acceptance → state=accepted.
6. **Document download.** From accepted quote → download AR document → opens viewer; share-sheet works.
7. **Locale switch.** Toggle quote document locale to EN → re-download → caches separately.
8. **Company register.** Open `/company/register` → submit → routes to company profile.
9. **Company profile read-only for non-admin.** Sign in as buyer → fields read-only.
10. **Branches.** As admin → add branch → list updates → delete → confirm → list updates.
11. **Invite user.** As admin → invite buyer@x.com as Approver → toast Sent.
12. **Invitation deep link.** Sign out, open `myapp://invitations/TOKEN_FROM_BACKEND` → app opens → company name + role shown → Accept → routes to company profile.
13. **Memberships.** As admin → change role → success → demote last admin attempt → server 409 with friendly message.
14. **Legacy quotations.** As an account with legacy quotes → list visible → detail → Accept → success.
15. **Legacy quotations empty.** Account without legacy quotes → menu entry hidden.

## Automated

```sh
flutter analyze
flutter test test/features/b2b/
```

## Troubleshooting

- **Two-step acceptance unclear:** verify the `actions.canSubmitAcceptance` + `actions.canFinalizeAcceptance` flags drive the toolbar; "Step 1 of 2" badge shows on submit-acceptance result.
- **Invitation deep link fails cold start:** `redirect_guard` must wait on `SessionStore != Unknown` before navigating.
- **Document download huge memory spike:** ensure Dio uses streaming response (`ResponseType.stream` + chunked sink to file).
