# Deep-link Routes

Universal-link / app-link route table. Hosted under the launch domain (assumed `dental-commerce.com` for prod, `staging.dental-commerce.com` for staging — final value owned by Phase 1E E1 infrastructure).

| Path | Screen | Auth required | Notes |
|---|---|---|---|
| `/` | Home | No | Default landing. |
| `/p/<productId>` | Product detail | No | Restricted-product gating still applies inside the screen. |
| `/c/<categoryId>` | Listing filtered by category | No | |
| `/search?q=…&market=…` | Listing with query | No | Market falls back to `MarketResolver` if absent. |
| `/cart` | Cart | No | Cart is anonymous-token-backed for guests. |
| `/checkout` | Checkout (continues active session) | Yes (gate) | If guest, redirect to `/auth/login?continueTo=/checkout`. |
| `/orders` | Orders list | Yes | |
| `/o/<orderId>` | Order detail | Yes | |
| `/auth/login` | Login | No (login screen itself) | Accepts `?continueTo=<encoded>`. |
| `/auth/register` | Register | No | Accepts `?continueTo=<encoded>`. |
| `/auth/otp` | OTP entry | No | Tied to a server-side challenge id from spec 004. |
| `/auth/reset?token=…` | Password reset confirm | No | One-shot link from spec 004 reset email. |
| `/auth/verify?token=…` | Email verification | No | One-shot link from spec 004 email verify. |
| `/more` | More menu | Yes | |
| `/more/addresses` | Address book | Yes | |
| `/more/verification` | Verification CTA | Yes | Placeholder until spec 020 ships. |

## App-link / universal-link manifests

- **Android** (`apps/customer_flutter/android/app/src/main/AndroidManifest.xml`): `<intent-filter android:autoVerify="true">` with the prod + staging hosts.
- **iOS** (`apps/customer_flutter/ios/Runner/Runner.entitlements`): `applinks:dental-commerce.com`, `applinks:staging.dental-commerce.com`.
- **`assetlinks.json` / `apple-app-site-association`**: hosted by the marketing site; coordination with Phase 1E E1 infra spec.

## Auth-resume semantics

`go_router`'s `redirect:` handler:

1. If the requested route is auth-required AND `AuthSessionBloc.state` is `Guest` / `RefreshFailed`:
   1. Push `/auth/login` with `?continueTo=<urlencoded original>`.
2. After successful login, the login screen reads `continueTo` from the query and replaces the navigation stack with the original target.
3. Cart state survives auth across the redirect because the anonymous cart token is exchanged for the authenticated cart server-side on first authenticated request (spec 009 contract).
