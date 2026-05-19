# Quickstart — Phase 1: Auth & Identity

## Prerequisites

- Flutter SDK matching `apps/customer_flutter/pubspec.yaml` constraints.
- Backend running locally OR pointed at a staging environment with `openapi.identity.json` deployed.
- iOS simulator or Android emulator.

## Local backend (recommended)

```sh
cd services/backend_api
dotnet run --project services/backend_api/Backend.Api.csproj
# default: http://localhost:5080
```

Set the customer app's base URL:

```sh
# from repo root
cp apps/customer_flutter/.env.example apps/customer_flutter/.env
echo "API_BASE_URL=http://localhost:5080" >> apps/customer_flutter/.env
```

## Run the mobile app

```sh
cd apps/customer_flutter
flutter pub get
flutter run             # picks first available device
# or target:
flutter run -d ios
flutter run -d android
```

## Manual smoke test (Phase 1 exit gate)

Execute on iOS **and** Android (record results in the PR description).

1. **Fresh install → register**
   1. Launch on a clean device/emulator (delete the app first).
   2. Tap Create Account. Fill name + email + phone + password. Accept terms. Submit.
   3. Expect OTP screen with masked destination.
   4. Use the backend logs (or the seeded OTP `123456` in dev mode) to enter the code.
   5. Land on `/home`.

2. **Sign out → sign in**
   1. From `/more`, tap Sign Out. Land on `/login`.
   2. Sign in with the credentials from step 1.
   3. Land on `/home`. Cold-start the app to verify session restores via Splash + refresh.

3. **Locale switch**
   1. From `/more` → Language & Market, switch from EN to AR. Save.
   2. Verify the current screen flips to RTL immediately; verify another screen (e.g., Cart) also renders RTL.
   3. Cold-start the app to verify the choice persists.

4. **Password reset**
   1. Sign out. Tap "Forgot password?".
   2. Enter identifier. Receive reset challenge (dev OTP `123456`).
   3. Enter code + new password. Submit.
   4. Land back on `/login` with toast. Sign in with new password.

5. **Email confirm deep link**
   1. While signed out, open `myapp://email-confirm?token=DEV_TOKEN_123` via simulator deep-link tooling (`xcrun simctl openurl booted` or `adb shell am start`).
   2. App opens to `/email-confirm` and shows the verified state with a "Sign in to continue" CTA.

6. **Sessions list**
   1. Sign in on two devices.
   2. On device A, open `/more/sessions`. Verify device B appears as "Other devices".
   3. Tap Revoke on device B. Verify the row updates.
   4. On device B, attempt any protected action → expect forced sign-out within 1–2 requests.

7. **Account security**
   1. From `/more/security`, change password (provide current + new).
   2. Verify success toast.
   3. Verify `/more/sessions` no longer shows the device-B session.

## Automated tests

```sh
cd apps/customer_flutter
flutter analyze        # zero warnings expected
flutter test           # all green
```

To run just the identity feature:

```sh
flutter test test/features/identity/
```

## Troubleshooting

- **"Invalid bearer" on cold start**: refresh-token expired server-side. Sign in again — this is expected after 30 days of inactivity (configured server-side).
- **OTP code rejected in dev**: the dev backend logs the actual code; do not assume `123456`. Check `services/backend_api/logs/*.log`.
- **AR text shows as boxes**: a font fallback issue. Ensure `pubspec.yaml` declares the AR-capable font under `flutter.fonts`.
- **Deep link does not open the app**: verify the URL scheme registration in `ios/Runner/Info.plist` (CFBundleURLSchemes) and `android/app/src/main/AndroidManifest.xml` (intent filter for `myapp`).
