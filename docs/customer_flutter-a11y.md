# customer_flutter — accessibility checklist

> Tracks per-screen WCAG 2.1 AA evidence for spec 014 (FR-006). The
> automated semantic walker (T118c) lives at
> `test/a11y/semantics_walker_test.dart`; this file documents the
> screen-by-screen sign-off owned by humans.

## Scope

Every screen reachable via `apps/customer_flutter/lib/features/**/screens/`
must satisfy:

- All actionable widgets carry a `Semantics(label: …)` or visible text.
- Focus order matches reading order in both LTR and RTL.
- Contrast: text ≥ 4.5:1, large text + UI ≥ 3:1.
- Tap targets ≥ 44×44 logical pixels.
- All form fields surface errors via `errorText` (announced by screen
  readers) — not just colour.
- No text-content is conveyed by colour alone.

## Screen evidence table

| Screen | Semantics labels | RTL focus order | Contrast | Tap targets | Errors announced | Notes |
|---|---|---|---|---|---|---|
| `/` Home | ☐ | ☐ | ☐ | ☐ | ☐ | banners + featured + tiles |
| `/p/<id>` Product detail | ☐ | ☐ | ☐ | ☐ | ☐ | restricted badge announced |
| `/c/<id>` Listing | ☐ | ☐ | ☐ | ☐ | ☐ | infinite scroll announced |
| `/search` Search | ☐ | ☐ | ☐ | ☐ | ☐ | facet drawer keyboard-traversable |
| `/cart` Cart | ☐ | ☐ | ☐ | ☐ | ☐ | qty stepper labels |
| `/auth/login` Login | ☐ | ☐ | ☐ | ☐ | ☐ | server errors via field `errorText` |
| `/auth/register` Register | ☐ | ☐ | ☐ | ☐ | ☐ | |
| `/auth/otp` OTP | ☐ | ☐ | ☐ | ☐ | ☐ | resend button announced when enabled |
| `/auth/reset` Reset request | ☐ | ☐ | ☐ | ☐ | ☐ | |
| `/auth/reset?token=…` Reset confirm | ☐ | ☐ | ☐ | ☐ | ☐ | |
| `/checkout` Checkout | ☐ | ☐ | ☐ | ☐ | ☐ | step transitions announced |
| `/checkout/drift` Drift | ☐ | ☐ | ☐ | ☐ | ☐ | drift summary in heading semantic |
| `/checkout/confirmation/<id>` Confirmation | ☐ | ☐ | ☐ | ☐ | ☐ | order number announced |
| `/orders` Orders list | ☐ | ☐ | ☐ | ☐ | ☐ | four state chips per row labelled |
| `/o/<id>` Order detail | ☐ | ☐ | ☐ | ☐ | ☐ | timeline events labelled |
| `/more` More menu | ☐ | ☐ | ☐ | ☐ | ☐ | language toggle announces both states |
| `/more/addresses` Address book | ☐ | ☐ | ☐ | ☐ | ☐ | per-market regex error announced |
| `/more/verification` Verification CTA | ☐ | ☐ | ☐ | ☐ | ☐ | placeholder body when not shipped |

## Sign-off

Owner: TBD · Date: TBD · Build: TBD · Linked PR: TBD

Tick the boxes only after running the automated walker (`flutter test
test/a11y/semantics_walker_test.dart`) AND a manual screen-reader pass on
both VoiceOver (iOS) and TalkBack (Android).
