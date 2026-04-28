# Feature Specification: Customer App Shell

**Feature Branch**: `phase-1C-specs`
**Created**: 2026-04-27
**Status**: Draft
**Input**: User description: "Spec 014 customer-app-shell (Phase 1C) — Flutter (Bloc) Android + iOS + web customer app shell. Per docs/implementation-plan.md §Phase 1C item 014: depends on 004–013 contracts merged to main; consumes Lane A backend (no inline backend changes — escalate gaps to the owning 1B spec). Exit criteria: shell, auth, home, listing, detail, cart, checkout, orders, more-menu screens; RTL + Arabic editorial pass."

## Clarifications

### Session 2026-04-27

- Q: When does the customer app require authentication? → A: At checkout only. Guests can browse and add non-restricted items to cart freely; auth is required when the customer taps **Proceed to checkout**. Restricted products still require auth + verification before add-to-cart.
- Q: How should the cart behave across multiple devices for a logged-in customer? → A: Server-synced single cart. One cart per authenticated customer; every device reads/writes the same cart. Last-write-wins on conflict, surfaced as a "cart updated elsewhere" notice when detected.
- Q: How should the order detail screen learn about server-side state changes? → A: Pull-to-refresh + open-time fetch. Re-fetch on every screen open and support manual pull-to-refresh. Live push is deferred to the notifications spec.
- Q: What is the minimum platform support matrix? → A: Standard Flutter 3.x defaults — iOS 14+, Android API 24+ (Android 7.0+), evergreen browsers (Chrome / Edge / Safari / Firefox last 2 versions).
- Q: What OTP delivery channel(s) should the customer shell support? → A: SMS + email, channel chosen by spec 004. Client renders the entry screen + resend timer and supports SMS auto-fill / clipboard paste on platforms that expose it. WhatsApp is deferred to the notifications spec.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Browse, add to cart, and complete a purchase (Priority: P1)

A guest opens the app, lands on the home screen, browses categories or searches for a product (in Arabic or English), opens a product detail, sees the price breakdown and any restricted-product badge, taps **Add to cart**, and proceeds to checkout. At checkout the app prompts auth (register / login / OTP); after successful auth, the customer completes the order and lands on a confirmation screen showing the new order number and a link to the order detail.

**Why this priority**: This is the conversion path. Without it the product has no business value; with just this story the customer app is already a viable MVP that turns visitors into paying customers. Every other story is supporting depth on top of this slice.

**Independent Test**: A clean install on each target platform (Android, iOS, web) can run the entire flow end-to-end against staging APIs, in both AR and EN, and produce a confirmed order visible in the admin (spec 018) — no other Phase 1C story needs to ship first.

**Acceptance Scenarios**:

1. **Given** a guest on a clean install, **When** they open the app, **Then** the home screen renders banners, featured sections, and category tiles within the perceived-instant loading budget without requiring authentication.
2. **Given** a guest on the listing screen, **When** they apply a facet, change sort order, or type a query in Arabic, **Then** results update without a page reload and respect the active facet/sort/query.
3. **Given** a guest viewing a restricted product detail, **When** the page loads, **Then** the price is visible AND a clearly labelled restricted badge is shown AND the **Add to cart** action communicates that verification is required before purchase.
4. **Given** a guest with items in the cart, **When** they proceed to checkout, **Then** the app gates the action behind register / login / OTP and resumes the cart state after successful auth.
5. **Given** an authenticated customer, **When** they complete checkout via a supported payment method for their market, **Then** they see an order confirmation screen with the order number and a deep link to its detail.
6. **Given** any of the above flows, **When** an underlying backend call fails or returns an empty result, **Then** the screen renders the corresponding loading / empty / error / restricted state — no blank or crashing screen.

---

### User Story 2 - Manage past orders and resolve issues (Priority: P2)

An authenticated customer opens the orders tab, sees their order history with all four state streams (order, payment, fulfillment, refund) visible at a glance, taps an order to see its detail timeline, taps **Reorder** to drop the same lines back into a new cart, or taps **Support** to start a help conversation about that specific order.

**Why this priority**: Drives repeat purchase and retention without which acquisition cost is wasted. Strictly post-MVP because a customer needs at least one order before this matters, but ships in the same launch window.

**Independent Test**: Create an order in staging via Story 1 (or any backend tooling), then verify the orders list and detail screens render the four state streams, the reorder action lands the same SKUs in a new cart, and the support shortcut opens a contact channel scoped to that order.

**Acceptance Scenarios**:

1. **Given** an authenticated customer with at least one order, **When** they open the orders tab, **Then** each row shows order state, payment state, fulfillment state, and refund state as separate signals (not collapsed into one badge).
2. **Given** an order in **fulfillment.handed_to_carrier**, **When** the customer opens its detail, **Then** they see the carrier reference, tracking link, and the timeline step that produced this state.
3. **Given** a delivered order, **When** the customer taps **Reorder**, **Then** all in-stock lines land in a fresh cart at the current price (out-of-stock lines are surfaced in an inline note, not silently dropped).
4. **Given** an order that the customer wants help with, **When** they tap **Support**, **Then** the support entry point opens prepopulated with the order reference.

---

### User Story 3 - Use the app comfortably in Arabic with full RTL (Priority: P3)

An Arabic-speaking customer toggles the language to Arabic from the more menu (or the app picks it up from the device locale on first launch), and every screen flips to RTL with editorial-grade Arabic copy, localized numerals/currency/dates, and no English fallbacks. Switching back to English instantly mirrors the UI back to LTR with English copy.

**Why this priority**: Constitution Principle 4 is non-negotiable for launch in EG/KSA, but the app can demonstrate value to internal stakeholders in EN-only mode while AR copy is being polished. Critical for the public launch but separable for early validation.

**Independent Test**: Open the app with device locale set to `ar-SA`, walk every screen reachable in Stories 1 + 2, and confirm: full RTL layout, no English string in any visible label, localized numerals/currency/dates, and editorial Arabic that reads naturally (not machine-translated).

**Acceptance Scenarios**:

1. **Given** the device locale is Arabic, **When** the customer launches the app for the first time, **Then** the UI renders in Arabic with RTL layout without requiring a manual toggle.
2. **Given** the customer is on any screen, **When** they switch language from the more menu, **Then** the active screen and all subsequent navigation render in the new language and direction without restart.
3. **Given** an Arabic UI, **When** the customer views prices, dates, or quantities, **Then** numerals, currency symbols, and date formats follow the active market's locale conventions (KSA → SAR, EG → EGP, Hijri-aware where applicable).
4. **Given** an Arabic UI, **When** the customer reaches any error / empty / restricted state, **Then** the copy is editorial-grade Arabic — never an English fallback or a raw error code.

---

### User Story 4 - Manage account, addresses, and verification from one place (Priority: P4)

An authenticated customer opens the more menu and finds: their saved addresses (view / add / edit / set default), language toggle, logout, and a verification CTA that takes restricted-product professionals into the verification submission flow.

**Why this priority**: Reduces support load and unblocks the verification path for professional buyers. Lower priority because the MVP can ship without it (a customer can still buy non-restricted items without verifying), but absent at launch it would force every B2B buyer to call support.

**Independent Test**: From the more menu, verify each entry resolves: address book opens with empty state when none exist and supports add/edit/default, language toggle reaches Story 3 acceptance, logout clears the session and returns to the home guest view, verification CTA opens the verification submission flow (or a "coming soon" placeholder until spec 020 ships — see Assumptions).

**Acceptance Scenarios**:

1. **Given** an authenticated customer with no saved addresses, **When** they open the address book, **Then** they see an empty state with a clear primary action to add their first address.
2. **Given** an authenticated customer, **When** they tap **Logout**, **Then** the session is cleared from secure storage and they land on the unauthenticated home view.
3. **Given** an authenticated customer who has not yet been verified, **When** they tap the verification CTA, **Then** the verification submission flow opens (or a placeholder consistent with the spec 020 status — see Assumptions).

---

### Edge Cases

- Network drops mid-checkout → cart and submitted-but-unconfirmed state must survive a retry without double-charging or losing items.
- Token expiry during a long browse session → silent refresh; user-visible re-auth prompt only when refresh fails.
- A product becomes restricted between **Add to cart** and checkout submit → the cart screen surfaces the restricted-eligibility error before payment is attempted.
- Stock disappears between cart view and submit → spec 010 drift detection surfaces; UI must render the drift screen, not a generic error.
- Locale switch with an in-flight network request → the in-flight request still completes, the response is rendered in the now-active locale.
- App opened on web with a deep link to a product detail → the detail loads directly without forcing a home-screen pass-through.
- A guest tries to add a restricted product → add-to-cart is gated with a clear message linking to register / login (verification happens after auth).
- A customer's market changes (e.g., they relocate KSA → EG) → prices, currency, payment methods, and shipping options follow the new market on the next session.
- Out-of-stock line during reorder → surfaced inline; in-stock lines still go into the new cart.
- App backgrounded for hours, foregrounded → cart, language, and session must restore cleanly without forcing re-login (until refresh-token expiry).

## Requirements *(mandatory)*

### Functional Requirements

#### Shell, navigation, theming

- **FR-001**: The customer app MUST run on Android, iOS, and web with no functional gap inside the screens listed in this spec. Minimum supported platforms: iOS 14+, Android API 24+ (Android 7.0+), and evergreen desktop browsers (Chrome, Edge, Safari, Firefox — current and previous major version). Platform parity is verified by the platform-parity matrix in `integration_test/platform_parity_test.dart`, which exercises every screen's primary action on each platform.
- **FR-002**: The shell MUST provide top-level navigation across home, listing, cart, orders, and more.
- **FR-002a**: The shell MUST accept universal / app links per `contracts/deeplink-routes.md`. Hosts: `dental-commerce.com` (prod), `staging.dental-commerce.com` (staging). Scheme: `https` only — no custom URL schemes. Auth-required deep links (e.g., `/orders/<id>`) gate via `go_router`'s `redirect:` and preserve the original path as `?continueTo=<urlencoded>` for post-login resume. Cross-install survival ("share before app install → install opens link") is **deferred** to a later spec; v1 ships with universal/app links only.
- **FR-003**: All screens MUST consume colour, typography, spacing, and component primitives from `packages/design_system` — no inline hard-coded design tokens.
- **FR-004**: The active palette MUST match Constitution Principle 7 (primary `#1F6F5F`, secondary `#2FA084`, accent `#6FCF97`, neutral `#EEEEEE`) plus the brand-overlay semantic colours.
- **FR-005**: Every screen MUST implement loading, empty, error, success, and (where applicable) restricted-state and payment-failure-recovery states per Constitution Principle 27.
- **FR-006**: Every screen MUST be keyboard-navigable on web and respect platform accessibility primitives (screen-reader labels, contrast, focus order). The accessibility bar is **WCAG 2.1 AA** — same target as the admin app per Constitution §27. Verification: per-screen accessibility checklist + a `flutter_test` `Semantics` walker asserting every `Text` has a non-empty `semanticsLabel` or visible content.

#### Localization, RTL, market awareness

- **FR-007**: Both Arabic and English MUST be fully supported across every screen in scope, with RTL layout active when Arabic is selected.
- **FR-008**: All user-visible copy MUST come from the localization layer; no English string may appear in the Arabic build.
- **FR-009**: Numerals, currency symbols, and dates MUST follow the active market's locale conventions. Locale switches mid-session MUST re-render the visible UI with the new locale's `intl` formatters. **Behaviour for in-flight network requests at locale-switch time depends on whether the endpoint is i18n-bearing** (carries server-localized strings) — see FR-009a. For non-i18n-bearing endpoints (cart contents, totals, ids, timestamps) the in-flight request MUST NOT be re-issued: its response is rendered through the now-active locale's formatters when it lands.
- **FR-009a**: An i18n-bearing endpoint is one whose response carries server-localized strings (product names + descriptions, CMS body, search results, order-timeline `reasonNote` strings, invoice metadata — i.e., spec 005, 006, 011, 012 surfaces). On a locale switch, the client MUST: (a) let any in-flight request to such an endpoint finish, **discard the response** (do not render it), and re-issue the request with the new `Accept-Language`; the current screen surfaces the localized loading skeleton during the brief gap. (b) Invalidate any cached response (Bloc state, repository memoization) for an i18n-bearing endpoint. Numerals / dates do not need re-fetching (formatters re-render), and pure-id / pure-numeric endpoints (cart contents, totals, line ids) do not either. The customer-app registry of i18n-bearing endpoints lives in `contracts/locale-aware-endpoints.md` (created with this FR — mirrors the admin app's registry in 015). Each consuming feature folder maintains its rows.
- **FR-010**: Arabic copy MUST be editorial-grade — copy approval gate before launch; machine-translated strings are non-compliant.
- **FR-011**: Market context (KSA, EG) MUST come from the customer's account, explicit selector, or device locale heuristic — never hard-coded in UI logic per Constitution Principle 5.

#### Auth + session

- **FR-012**: The app MUST allow unauthenticated browsing of home, listing, detail, search, and prices per Constitution Principle 3.
- **FR-013**: The app MUST allow guests to add non-restricted items to cart without auth. Authentication MUST be required only at: (a) the **Proceed to checkout** action, (b) any orders / addresses / verification surface, and (c) add-to-cart for any product flagged as restricted (verification status is checked after the auth step). The guest cart MUST be preserved across the auth step (anonymous cart token surviving the login event).
- **FR-013a**: On a successful authentication event (register / login / OTP verify / password reset), the app MUST submit the anonymous cart token to spec 009's cart-claim endpoint. Spec 009 is server-authoritative on the merge: it returns the authenticated cart with merged lines plus a list of per-line conflict reports (e.g., `now_restricted_and_unverified`, `out_of_stock`, `quantity_capped`). The client renders the merged cart and surfaces conflicts in a single non-blocking "cart updated after sign-in" banner — never dropping lines silently. Cart submit (checkout) MUST remain allowed unless an unresolved conflict (e.g., a restricted line on an unverified account) blocks it via the existing FR-022 / FR-021a gates.
- **FR-013b**: Until spec 009's cart-claim endpoint documents its policy, the **expected merge contract** the client codes against is: (a) per-SKU quantities are summed up to per-product `max_qty_per_order` (excess clamped, surfaced as `quantity_capped`); (b) line-level metadata (custom notes) takes the guest-cart value when present; (c) restricted-product lines added as a guest by a now-unverified user are kept in the cart with a `now_restricted_and_unverified` flag (surfaced via FR-021a's banner) rather than dropped; (d) out-of-stock lines are kept with a `out_of_stock` flag. If spec 009 ships a different policy, the client adopts it — this FR documents the contract the client expects in the absence of one and exists so an absent / partial spec 009 endpoint doesn't block 014.
- **FR-014**: Auth screens MUST cover register, login, OTP verification, and password reset, all wired to spec 004 contracts. The OTP entry screen MUST support both SMS and email channels (channel selection driven by spec 004's response, not chosen by the client), display a resend timer that respects the spec 004 rate-limit window, and where the platform exposes it, support SMS auto-fill (Android `SmsRetriever` / iOS `oneTimeCode` AutoFill) and clipboard paste of the code. The resend timer MUST persist server-side state (i.e., the timer is driven by spec 004's response, not a client-only countdown). A user closing and reopening the app, or switching tabs on web, MUST see the same remaining cooldown — never a reset that bypasses spec 004's rate limit.
- **FR-015**: Session tokens MUST persist in secure storage and survive app relaunch until refresh-token expiry.
- **FR-015a**: When `flutter_secure_storage`'s schema or key set changes between app versions, the app MUST migrate cleanly without forcing a logout. The migration runs once on first launch of a new version: read all keys under the previous schema, re-write under the new schema, delete the old keys. If migration fails (e.g., previous keys unreadable due to OS-keychain reset), the app falls back to a clean guest session — never crashes. A migration entry is logged via `TelemetryAdapter` (`auth.storage.migrated` with `from_version`, `to_version`, `outcome`).
- **FR-015b**: Local development uses `http://localhost:5000` — tokens stored under `flutter_secure_storage`'s web adapter (`EncryptedLocalStorage` per research §R5) on the web target are visible in browser DevTools when running over HTTP. This is **dev-only**. The app MUST refuse to attach `Authorization: Bearer …` to a non-`https` request URL unless an explicit `--dart-define=ALLOW_INSECURE_BACKEND=1` build flag is set (default off in release builds, on in `flutter run` debug builds). Release builds enforce HTTPS unconditionally.
- **FR-016**: Logout MUST clear all session material from secure storage and return the user to the unauthenticated home view.
- **FR-017**: Token refresh MUST be silent on the foreground; only a refresh-failure event surfaces a re-auth prompt to the user.

#### Home, listing, detail

- **FR-018**: The home screen MUST render banners, featured sections, and category tiles consumed from the CMS / catalog contracts (spec 005 plus the spec 022 CMS stub — see Assumptions). "Perceived-instant" loading budget for the home means: **first contentful paint ≤ 800 ms** on the SC-006 reference devices, with skeleton placeholders rendering at frame one and real content swapping in within the budget. Anything above 800 ms surfaces the loading state explicitly (skeleton stays visible) — no white flash, no jank.
- **FR-019**: The listing screen MUST support facets, sort, and Arabic-tolerant search via spec 006 contracts.
- **FR-020**: The product detail screen MUST display the media gallery, attribute specs, restricted badge when applicable (with prices still visible per Constitution Principle 8), and the price breakdown returned by spec 007.
- **FR-021**: When a product is restricted and the user is unauthenticated or unverified, the **Add to cart** action MUST be gated with copy that explains the verification requirement and routes to register / login / verification — never silently disabled.
- **FR-021a**: When an authenticated customer's verification status is revoked (or expires) **between sessions** while a restricted-product line sits in their server-synced cart, the cart screen on next mount MUST surface a non-blocking "verification required for some items" banner listing the affected lines, leave the lines in place (the customer can resolve verification or remove them), and **block checkout submission** until either verification is restored or the restricted lines are removed. The block surfaces via spec 009's restricted-eligibility signal — consumed by this spec's cart screen per FR-022 below. This FR codifies the cross-session timing that FR-013 / FR-013a / FR-013b do not cover (those handle in-session add-to-cart and login-time merge respectively).

#### Cart, checkout, post-purchase

- **FR-022**: The cart screen MUST consume spec 009 contracts and surface restricted-eligibility / stock-availability signals returned by the cart service.
- **FR-022a**: The cart MUST be server-synced across an authenticated customer's devices — phone, web, and any future surface read and write the same cart. When the cart screen detects a server-side change made by another device (e.g. a server response carries a newer revision than the local view-model), it MUST surface a non-blocking "cart updated elsewhere" notice and refresh the displayed lines before allowing destructive actions (remove, change quantity).
- **FR-023**: The checkout flow MUST consume spec 010 contracts including drift detection, idempotency on submit, and the four-state outcome model.
- **FR-024**: Supported payment methods on the checkout screen MUST be derived from the per-market payment-method catalog (spec 010 / Constitution Principle 13) — no hard-coded list in the UI.
- **FR-025**: The orders list MUST display order, payment, fulfillment, and refund states as four independent signals per Constitution Principle 17 + spec 011.
- **FR-026**: Order detail MUST surface the timeline of all four state streams plus carrier tracking link when present. Freshness MUST be guaranteed by re-fetching on every screen open and supporting pull-to-refresh; live server-push of order events is out of scope here and is owned by the notifications spec.
- **FR-027**: Order detail MUST expose **Reorder** and **Support** actions; reorder produces a fresh cart with in-stock lines and an inline note for any out-of-stock line.

#### More menu

- **FR-028**: The more menu MUST expose: addresses, language, logout, and verification CTA.
- **FR-029**: The address book MUST support view, add, edit, delete, and set-default operations.
- **FR-030**: The language toggle MUST switch the active locale and direction for the running session without a relaunch.

#### Architectural guardrails

- **FR-031**: This spec MUST NOT modify any backend contract. Any gap discovered while implementing the screens MUST be raised as an issue against the owning Phase 1B spec, not patched in this PR (per Phase 1C intent in the implementation plan).
- **FR-032**: State management MUST follow Bloc / `flutter_bloc` per ADR-002 — no Riverpod / Provider / GetX in this app.
- **FR-033**: All API access MUST go through generated clients consuming the merged spec 004–013 contracts; no ad-hoc HTTP calls inside screen code.

### Key Entities *(client-side state, no backend persistence)*

- **Session**: refresh + access token pair, expiry, customer id, market code, active locale.
- **Cart view-model**: line items, restricted-eligibility signals per line, stock-availability signals, totals breakdown from spec 007.
- **Checkout view-model**: shipping address, selected shipping method, selected payment method, drift-detection state, idempotency key.
- **Order view-model**: order id, four state-stream values, line items, totals, carrier tracking, timeline events.
- **Address**: label, recipient, line, city, market, default-flag.
- **Locale + market**: active language, active market, currency, RTL flag.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new customer can complete a purchase end-to-end (home → listing → detail → cart → register/login/OTP → checkout → confirmation) in under 4 minutes on a mid-tier Android device on a 4G network.
- **SC-002**: ≥ 95 % of customers who reach the checkout screen complete the order without hitting a generic / un-localized error state.
- **SC-003**: 100 % of screens in scope render correctly in both Arabic-RTL and English-LTR — measured by a launch-blocker checklist that walks every screen in both locales on every target platform.
- **SC-004**: 0 user-visible English strings on any screen when the active locale is Arabic.
- **SC-005**: ≥ 90 % of repeat customers find their orders list within 2 taps from the cold-launched app, measured against the spec's defined navigation tree.
- **SC-006**: Cold app launch to interactive home screen ≤ 3 seconds on the reference Android device, ≤ 2 seconds on the reference iOS device, and ≤ 4 seconds on web (broadband).
- **SC-007**: Reorder action on a previously-delivered order produces a cart whose line items match the original order's in-stock subset 100 % of the time, with out-of-stock lines surfaced as an explicit inline note.
- **SC-008**: 0 backend contract changes shipped from this spec — escalations to the owning Phase 1B spec are tracked as separate issues.

## Assumptions

- **CMS dependency**: The home screen's banners and featured sections consume the CMS contract from spec 022. Until spec 022 ships, the home screen consumes a stub backed by static curated catalog content (spec 005 categories + featured products) so this spec can ship without blocking on 022.
- **Verification dependency**: The verification CTA in the more menu links into the verification submission flow from spec 020. Until spec 020 ships, the CTA opens a "verification coming soon" placeholder rather than being hidden — keeping the discovery affordance intact and reducing churn when 020 ships.
- **Reviews out of scope**: Customer-facing review submission and listing UI are part of a later spec (Constitution Principle 15). Detail screens will reserve layout space for review summary but render an empty placeholder until the reviews spec ships.
- **B2B UI deferred**: Quotation requests, company-account switching, approver flows, and bulk ordering UI are tracked under spec 021 (B2B), not this spec. The customer shell here serves the consumer + verified-professional path; B2B-only screens come later.
- **Market default**: Until spec 020's market-from-account is wired, the app derives market from device locale: `ar-SA` / `en-SA` → KSA, `ar-EG` / `en-EG` → EG, anything else → KSA (the primary residency per ADR-010). The address book entry forces market consistency once the customer adds their first address.
- **Web build target**: Flutter web in CanvasKit renderer for visual fidelity; the deferred decision on whether the web build is hosted as static (Azure Static Web Apps) or as a container is part of Phase 1E E1 and does not block this spec.
- **Reference devices for performance targets**: Android — Pixel 6a (or equivalent A-series mid-tier); iOS — iPhone 13; web — Chrome on a 2022-era laptop on broadband.
- **Lane-A handoff**: All depended-on backend specs (004–013) ship and merge their contracts (not just DoD) before this spec begins implementation, per the implementation plan's Lane A → Lane B rule.
- **No backend code in this PR**: Backend gaps surfaced during implementation are filed as issues against the owning 1B spec; this spec ships UI only.

## Dependencies

- **Spec 004 (identity)** — auth, session, refresh
- **Spec 005 (catalog)** — categories, brands, products, restriction metadata
- **Spec 006 (search)** — Arabic-tolerant search, facets, autocomplete
- **Spec 007 (pricing & tax engine)** — price breakdown
- **Spec 008 (inventory)** — stock signals
- **Spec 009 (cart)** — cart contracts
- **Spec 010 (checkout)** — checkout flow + drift + idempotency
- **Spec 011 (orders)** — order list + detail + four state streams
- **Spec 012 (tax invoices)** — invoice download from order detail
- **Spec 013 (returns)** — return-request initiation from order detail
- **Spec 022 (CMS)** — home banners + featured sections (stub until 022 ships — see Assumptions)
- **Spec 020 (verification)** — verification CTA target (placeholder until 020 ships — see Assumptions)
- **`packages/design_system`** — design tokens + base components

## Out of Scope (this spec)

- Admin web (separate spec 015 onward).
- B2B-specific UI (quotation, company accounts, approvers, bulk order — spec 021).
- Customer review submission UI (later spec).
- Push-notification UI surfaces (spec 019 / 023 area).
- Loyalty programme UI.
- In-app chat support beyond the per-order support shortcut (separate spec 023 area).
