# DoD Checklist — Phase 4: Cart & Checkout

## Cart

- [ ] `CartStore` with atomic persistence; clears on sign-out.
- [ ] `CartBloc` debounces price-cart 300 ms.
- [ ] Drift-line UX (strikethrough + remove).
- [ ] Coupon apply with optimistic UI + 422 rollback.

## Checkout gateway + base

- [ ] `CheckoutGateway` covers 8 endpoints with typed responses.
- [ ] `CheckoutBaseBloc` drift handler resolves 409 via reusable `ConflictDialog`.

## Step screens

- [ ] S-4.3 Start: single POST; 409 handling; session id persisted.
- [ ] S-4.4 Summary: stepper reflects `stepStatus`; pull-to-refresh.
- [ ] S-4.5 Address: pre-fill, E.164 phone normalization, picker reads saved addresses.
- [ ] S-4.6 Shipping: empty quotes branch; radio selection.
- [ ] S-4.7 Payment: server-driven method list; adapter per method; SAQ-A (no PAN/CVV in app memory).
- [ ] S-4.8 Review: Idempotency-Key generated once; same on retry; 3DS / WebView return handled.
- [ ] S-4.9 Drift: reusable dialog; accept-drift then retry.
- [ ] S-4.10 Confirmation: cart cleared; bank-transfer reference copyable.

## Cross-cutting

- [ ] 3DS / WebView return resumes the session on iOS + Android.
- [ ] Cold-start resume works (`SessionStore.checkoutSessionId`).
- [ ] PCI scope CI guard greps for `cardNumber|cvv|pan` outside `payment_adapters/`.

## Phase exit

- [ ] `flutter analyze` clean.
- [ ] `flutter test` green.
- [ ] Smoke test from `quickstart.md` recorded on iOS + Android.
- [ ] §8 row → **Done**.
