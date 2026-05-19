# DoD Checklist — Phase 1: Auth & Identity

Phase exit gate. All boxes must be ticked before flipping the §8 status row in `docs/mobile-app-screen-api-plan.md` to **Done**.

## Foundation

- [ ] `core/error/failure.dart` + `error_mapper.dart` map every status code listed in `data-model.md` to a typed `Failure`.
- [ ] `core/session/session_store.dart` persists across cold start; `Stream<SessionState>` emits `Unknown → Authenticated|Anonymous` correctly.
- [ ] All five interceptors (`auth`, `localeMarket`, `correlationId`, `idempotency`, error mapper) are registered in the IDN-alias Dio instance.
- [ ] Auth interceptor performs at most one refresh per 401 and clears store on the second 401.
- [ ] Idempotency-Key is attached **only** for ops in the opt-in matrix (not register/sign-in by default; this phase has no idempotency-required ops).
- [ ] `core/theme/colors.dart` exports the Principle 7 palette verbatim.
- [ ] AR + EN themes build clean.
- [ ] `core/router/app_router.dart` registers every route from `plan.md`; redirect guard honors `redirectTo`.
- [ ] `lib/features/identity/data/identity_gateway.dart` (interface) and impl exist; every Bloc uses the interface, never raw `Dio`.

## Screens

### S-1.1 Splash
- [ ] Renders ≤ 200 ms brand splash before spinner.
- [ ] Single refresh attempt; on failure clears store.
- [ ] Routes to `/home` on Authenticated, `/login` on Anonymous.
- [ ] 5xx / offline shows retry banner with correlation-id.
- [ ] AR + EN smoke pass.
- [ ] Bloc unit tests + widget tests for every state.

### S-1.3 Login
- [ ] Submit disabled until both fields are non-empty.
- [ ] Tokens persisted before navigation.
- [ ] `redirectTo` honored when present.
- [ ] MFA branch routes to `/otp?intent=mfa`.
- [ ] 401/403/422/429/5xx/offline UI states implemented.
- [ ] AR copy editorial.
- [ ] Bloc unit tests + widget tests.

### S-1.4 Register
- [ ] Terms checkbox required.
- [ ] Live password-rules checklist.
- [ ] Phone normalized to E.164 by market.
- [ ] 409 banner with Sign-in deep-link.
- [ ] Routes to `/otp?intent=register`.
- [ ] AR copy editorial.
- [ ] Tests.

### S-1.5 OTP
- [ ] Auto-submit on 6th digit; paste populates all boxes.
- [ ] Resend countdown rendered from server payload (not wall clock).
- [ ] Expiry recomputed from `expiresAt` on resume.
- [ ] Intent-based post-success routing.
- [ ] AR copy editorial.
- [ ] Tests.

### S-1.6 / S-1.7 Password reset
- [ ] Two routes registered: `/password-reset`, `/password-reset/complete`.
- [ ] Request screen masks destination on success.
- [ ] Complete screen shows rules checklist; on success routes to `/login` with toast (no auto-sign-in).
- [ ] Expired-challenge banner with CTA to restart.
- [ ] AR copy editorial.
- [ ] Tests.

### S-1.8 Email confirm
- [ ] Deep-link route registered.
- [ ] Token consumed once; subsequent attempts show success-already state.
- [ ] Authenticated branch routes to `/home`; anonymous branch shows "Sign in to continue".
- [ ] AR copy editorial.
- [ ] Tests.

### S-1.9 Locale & Market
- [ ] PATCH persists, then app rebuilds with new locale/market without restart.
- [ ] Sibling screen visibly flips text direction.
- [ ] AR copy editorial.
- [ ] Tests.

### S-1.10 More hub + Account Security
- [ ] Menu items navigate to Phase 1 destinations; placeholders for Phase 7/8 destinations.
- [ ] Sign Out clears `SessionStore` before routing.
- [ ] Account Security: current+new+confirm; success toast mentions other-sessions invalidation.
- [ ] AR copy editorial.
- [ ] Tests.

### S-1.11 Sessions
- [ ] Lists sessions grouped (This device / Other devices).
- [ ] Revoke current device → forced sign-out.
- [ ] Revoke other device → optimistic update; 409 silently refreshes.
- [ ] Pull-to-refresh works.
- [ ] AR copy editorial.
- [ ] Tests.

## Phase exit

- [ ] `flutter analyze` zero warnings.
- [ ] `flutter test` passes.
- [ ] Smoke test from `quickstart.md` executed on iOS + Android; results pasted into the PR description.
- [ ] `docs/mobile-app-screen-api-plan.md` §8 row for Phase 1 flipped to **Done**.
- [ ] No edits made under `services/backend_api/**`, `apps/admin_web/**`, `packages/design_system/**` as part of this PR (changes scoped to mobile + docs).
