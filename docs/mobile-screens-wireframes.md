# Mobile App — Customer Screen Wireframes (ASCII reference)

> **Scope:** customer mobile app only (`apps/customer_flutter/`). Each wireframe carries a stable anchor ID (`#phase-N-screen-slug`) that the matching screen entry in `specs/mobile/phase-N-*/spec.md` links to.
>
> Wireframes are **layout reference**, not production design. Brand palette (Principle 7: `#1F6F5F` / `#2FA084` / `#6FCF97` / `#EEEEEE`), typography, RTL mirroring, restricted-product UX, B2B vs consumer pricing, error/empty/loading variants, and accessibility live in the design system + the spec entry's UI-states table.

## Wireframe conventions

- Each screen is a full mobile frame.
- Components are explicit: logo, inputs, buttons, links, lists, cards, actions.
- Main shell screens include bottom nav: `Home | Categories | Cart | Orders | More`.
- Auth, one-time verification, and deep-link confirmation screens hide the bottom nav.

### Bottom nav

```text
+--------------------------------------+
| Home | Categories | Cart | Orders | More |
+--------------------------------------+
```

| Tab | Phase | Owning screen |
|---|---|---|
| Home | 2 | S-2.1 catalog home |
| Categories | 2 | S-2.2 categories list |
| Cart | 4 | S-4.1 cart |
| Orders | 5 | S-5.1 orders list |
| More | 1 / 7 | S-1.10 more hub (locale, sessions, verification CTA, reviews, b2b entry) |

---

## Phase 1 — Auth & Identity

### #phase-1-splash · S-1.1 Splash / session bootstrap

```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
|                                      |
|                 LOGO                 |
|                                      |
|            Loading session...        |
|                                      |
|        [ spinner animation ]         |
|                                      |
|      Checking token and profile      |
|                                      |
|                                      |
|                                      |
|                                      |
+--------------------------------------+
| Retry                               >|
+--------------------------------------+
```

### #phase-1-login · S-1.3 Login

```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
|                                      |
|                 LOGO                 |
|                                      |
|               Welcome back           |
|                                      |
|  Email or Phone                      |
|  [_______________________________]   |
|                                      |
|  Password                            |
|  [_______________________________]   |
|                      [ Show / Hide ] |
|                                      |
|  [            Login Button         ] |
|                                      |
|  Register                            |
|  [          Create Account         ] |
|                                      |
|  Forgot password?                    |
|  (link) Reset Password               |
+--------------------------------------+
```

### #phase-1-register · S-1.4 Register

```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
|                 LOGO                 |
|            Create Account            |
|                                      |
|  Full Name                           |
|  [_______________________________]   |
|  Email                               |
|  [_______________________________]   |
|  Phone                               |
|  [_______________________________]   |
|  Password                            |
|  [_______________________________]   |
|                                      |
|  [ ] I agree to Terms and Privacy    |
|                                      |
|  [          Register Button        ] |
|                                      |
|  Already have account? (link) Login  |
+--------------------------------------+
```

### #phase-1-otp · S-1.5 OTP request & verify

```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
|               Verify OTP             |
|  Code sent to +966******42           |
|                                      |
|        [_][_][_][_][_][_]            |
|                                      |
|  Resend in 00:28                     |
|  (link) Resend code                  |
|                                      |
|  [          Verify Button          ] |
|                                      |
|  (link) Change phone/email           |
+--------------------------------------+
```

### #phase-1-pwd-reset-request · S-1.6 Password reset — request

```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
|             Reset Password           |
|                                      |
|  Email or Phone                      |
|  [_______________________________]   |
|                                      |
|  [       Send Reset Link/Button    ] |
|                                      |
|  (link) Back to Login                |
+--------------------------------------+
```

### #phase-1-pwd-reset-complete · S-1.7 Password reset — set new

```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
|            Set New Password          |
|                                      |
|  New Password                        |
|  [_______________________________]   |
|  Confirm Password                    |
|  [_______________________________]   |
|                                      |
|  [         Save Password           ] |
|                                      |
|  Password rules checklist            |
+--------------------------------------+
```

### #phase-1-email-confirm · S-1.8 Email confirmation deep link

```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
|                                      |
|                 LOGO                 |
|                                      |
|        Email Verified Successfully   |
|                                      |
|  Your account is now confirmed.      |
|                                      |
|  [            Continue             ] |
+--------------------------------------+
```

### #phase-1-account-security · S-1.10 Account security

```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
| < Account Security              Save |
+--------------------------------------+
| Change Password                       |
| Current [_________________________]   |
| New     [_________________________]   |
| Confirm [_________________________]   |
| [       Update Password           ]   |
|--------------------------------------|
| Sessions                              |
| iPhone 15 Pro        Active Now       |
| Mac Safari           Yesterday        |
|--------------------------------------|
| [             Sign Out             ]  |
+--------------------------------------+
```

### #phase-1-sessions · S-1.11 Device / session management

```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
| < Active Sessions                    |
+--------------------------------------+
| This device                           |
| iPhone 15 Pro / Riyadh / Now         |
|--------------------------------------|
| Other devices                         |
| iPad / Riyadh / 2h ago       [Revoke]|
| Chrome / Jeddah / 1d ago     [Revoke]|
|--------------------------------------|
| [      Revoke All Other Sessions   ] |
+--------------------------------------+
```

### #phase-1-locale · S-1.9 Locale & market

```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
| < Language & Market                  |
+--------------------------------------+
| Language                              |
| [ English (EN)                    v ]|
|                                      |
| Market                                |
| [ SA (KSA)                        v ]|
|                                      |
| Currency                              |
| [ SAR                             v ]|
|                                      |
|  [             Save Changes        ] |
+--------------------------------------+
```

---

## Phase 2 — Catalog

### #phase-2-home · S-2.1 Home

```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
| [ Search products, brands...      ]  |
|--------------------------------------|
| Categories                            |
| [Tile][Tile][Tile][Tile]             |
|--------------------------------------|
| Brands                                |
| [Brand][Brand][Brand][Brand]         |
|--------------------------------------|
| Featured                              |
| [Product card]                        |
| [Product card]                        |
+--------------------------------------+
| Home | Categories | Cart | Orders | More |
+--------------------------------------+
```

### #phase-2-categories · S-2.2 Categories list

```text
+--------------------------------------+
| < Categories                         |
| [Category tile] [Category tile]      |
| [Category tile] [Category tile]      |
| [Category tile] [Category tile]      |
+--------------------------------------+
```

### #phase-2-category-detail · S-2.3 Category detail

```text
+--------------------------------------+
| < Category: Bathroom Tiles           |
| [Filter chips.....................]  |
|--------------------------------------|
| [P1] [P2]                            |
| [P3] [P4]                            |
| [P5] [P6]                            |
+--------------------------------------+
```

### #phase-2-brands · S-2.4 Brand list

```text
+--------------------------------------+
| < Brands                             |
| [Brand tile]   [Brand tile]          |
| [Brand tile]   [Brand tile]          |
+--------------------------------------+
```

### #phase-2-product-list · S-2.5 Product list (by category/brand)

```text
+--------------------------------------+
| < Products: Brand X                  |
| [Filter] [Sort] [Brand] [Price]      |
|--------------------------------------|
| [Product Card: image/title/price]    |
|  ★ 4.7 (125)   [Add ▾]               |
| [Product Card: image/title/price]    |
| [Product Card: image/title/price]    |
|--------------------------------------|
| [            Load More             ] |
+--------------------------------------+
```

### #phase-2-product-detail · S-2.6 Product detail (PDP)

```text
+--------------------------------------+
| < Product Detail                     |
| [ image gallery .................. ] |
| Product Name                          |
| Price: 120 SAR                        |
| Stock: In Stock                       |
| Rating: ★ 4.7 (125)                   |
| [ Restricted? Requires verification ] |  ← Principle 8
|--------------------------------------|
| Description                           |
| lorem ipsum ...                       |
|--------------------------------------|
| Qty [-] [1] [+]                       |
| [           Add To Cart            ]  |
+--------------------------------------+
```

### #phase-2-rating-summary · S-2.7 Rating summary block

```text
+--------------------------------------+
| Rating Summary                        |
| 4.7 ★★★★★                             |
| 125 reviews                           |
| 5★ ████████                           |
| 4★ █████                              |
| 3★ ██                                 |
+--------------------------------------+
```

### #phase-2-stock-badge · S-2.8 Stock badge

```text
+--------------------------------------+
| Availability                          |
| [ IN STOCK ]                          |
| Delivery by: Tomorrow                 |
+--------------------------------------+
```

---

## Phase 3 — Search

### #phase-3-search-entry · S-3.1 Search entry

```text
+--------------------------------------+
| < Search                             |
| [ Search input                     ] |
|--------------------------------------|
| Recent searches                       |
| - paint white matte                   |
| - ceramic tile                        |
+--------------------------------------+
```

### #phase-3-autocomplete · S-3.2 Autocomplete

```text
+--------------------------------------+
| < Search                             |
| [ tile|                            ] |
|--------------------------------------|
| Suggestions                           |
| - cement board                        |
| - ceramic tile                        |
| - steel profile                       |
|--------------------------------------|
| Top product matches                   |
| [Mini card] [Mini card]              |
+--------------------------------------+
```

### #phase-3-results · S-3.3 Search results

```text
+--------------------------------------+
| < Results for "tile"                 |
| [Filter] [Sort] [Brand] [Price]      |
|--------------------------------------|
| Facets:                              |
|   Brand: [X][ ][ ]                   |
|   Price: 0─[====]─500                |
|--------------------------------------|
| [Product Card: image/title/price]    |
| [Product Card: image/title/price]    |
| [Product Card: image/title/price]    |
|--------------------------------------|
| [            Load More             ] |
+--------------------------------------+
```

### #phase-3-lookup · S-3.4 Lookup (SKU/barcode)

```text
+--------------------------------------+
| < Lookup                             |
| [ Scan barcode ] [ Enter SKU      ]  |
| [_______________________________]    |
|                                      |
| Result:                              |
|   Product Name                        |
|   SKU 123-456                         |
|   [Open product]                      |
+--------------------------------------+
```

---

## Phase 4 — Cart & Checkout

### #phase-4-cart · S-4.1 Cart

```text
+--------------------------------------+
| < Cart                               |
| [Cart line item] qty [-][1][+]       |
| [Cart line item] qty [-][2][+]       |
|--------------------------------------|
| Promo code [________] [Apply]        |
|--------------------------------------|
| Subtotal              :  240 SAR     |
| Discount              : - 20 SAR     |
| VAT                   :   33 SAR     |
| Total                 :  253 SAR     |
| [        Proceed to Checkout       ] |
+--------------------------------------+
| Home | Categories | Cart | Orders | More |
+--------------------------------------+
```

### #phase-4-checkout-start · S-4.3 Checkout start

```text
+--------------------------------------+
| < Checkout                           |
| Order summary snapshot               |
| Address: not set                     |
| Payment: not set                     |
| [          Start Checkout          ] |
+--------------------------------------+
```

### #phase-4-summary · S-4.4 Checkout summary

```text
+--------------------------------------+
| < Checkout Summary                   |
| Stepper: Address > Shipping > Pay    |
| Items list                            |
| Totals                                |
| [              Continue             ] |
+--------------------------------------+
```

### #phase-4-address · S-4.5 Address step

```text
+--------------------------------------+
| < Shipping Address                   |
| Name   [__________________________]  |
| Phone  [__________________________]  |
| City   [__________________________]  |
| Street [__________________________]  |
| [         Save and Continue        ] |
+--------------------------------------+
```

### #phase-4-shipping-quotes · S-4.6 Shipping step

```text
+--------------------------------------+
| < Shipping Methods                   |
| ( ) Standard 2-3 days   15 SAR       |
| ( ) Express  1 day      30 SAR       |
| [              Select               ] |
+--------------------------------------+
```

### #phase-4-payment · S-4.7 Payment step

```text
+--------------------------------------+
| < Payment Method                     |
| ( ) Card                             |
| ( ) Apple Pay / Mada / STC Pay       |
| ( ) Bank transfer                    |
| ( ) COD (if eligible)                |
| Card No [_________________________]  |
| [         Continue                 ] |
+--------------------------------------+
```

### #phase-4-review-submit · S-4.8 Order review / submit

```text
+--------------------------------------+
| < Review and Place Order             |
| Final totals + shipping + payment    |
| [Idempotency-Key: <auto>]            |
| [            Place Order           ] |
+--------------------------------------+
```

### #phase-4-drift · S-4.9 Drift / 409 conflict

```text
+--------------------------------------+
|         Prices have changed          |
|                                      |
|  Old total : 253 SAR                 |
|  New total : 261 SAR                 |
|                                      |
|  Some items moved or repriced.       |
|                                      |
|  [Review changes] [Accept and pay]   |
+--------------------------------------+
```

### #phase-4-confirmation · S-4.10 Order confirmation

```text
+--------------------------------------+
|              Success                  |
| Order #123456 created                 |
| [           View Order              ] |
| [        Continue Shopping          ] |
+--------------------------------------+
```

---

## Phase 5 — Orders

### #phase-5-orders-list · S-5.1 Orders list

```text
+--------------------------------------+
| < My Orders                          |
| [All][Pending][Delivered]            |
| Order card #1                        |
|   Items 3 · Total 253 SAR            |
|   Payment: Paid · Fulfillment: Picked|
| Order card #2                        |
+--------------------------------------+
| Home | Categories | Cart | Orders | More |
+--------------------------------------+
```

### #phase-5-order-detail · S-5.2 Order detail

```text
+--------------------------------------+
| < Order #123456                      |
| State machines (4 pills):            |
|   [Order: Confirmed]                 |
|   [Payment: Paid]                    |
|   [Fulfillment: Picked]              |
|   [Refund: —]                        |
|--------------------------------------|
| Timeline                              |
|  ● Placed  Apr 10                    |
|  ● Picked  Apr 11                    |
|  ○ Shipped — pending                  |
|--------------------------------------|
| Items / payment / shipping            |
| [Cancel] [Return] [Reorder] [Retry pay]
+--------------------------------------+
```

### #phase-5-cancel · S-5.3 Cancel order

```text
+--------------------------------------+
| < Cancel Order                       |
| Reason [v]                            |
| Note   [__________________________]   |
| [          Confirm Cancel          ]  |
+--------------------------------------+
```

### #phase-5-reorder · S-5.4 Reorder

```text
+--------------------------------------+
| < Reorder                            |
| Previous items list                   |
| Qty controls                          |
| [            Add All to Cart        ] |
+--------------------------------------+
```

### #phase-5-tracking · S-5.5 Tracking timeline

```text
+--------------------------------------+
| < Tracking #SHP-789                   |
| ● Picked up    10:02                  |
| ● At hub       12:30                  |
| ○ Out for delivery                    |
| ○ Delivered                           |
+--------------------------------------+
```

---

## Phase 6 — Returns & Invoices

### #phase-6-returns-list · S-6.1 Returns list

```text
+--------------------------------------+
| < My Returns                         |
| Return #R1001  Pending               |
| Return #R1002  Approved              |
+--------------------------------------+
```

### #phase-6-return-eligibility · S-6.2a Return eligibility (entry)

```text
+--------------------------------------+
| < Return Eligibility                 |
| Eligible items checklist              |
| Policy notice                         |
| [          Continue Return          ] |
+--------------------------------------+
```

### #phase-6-return-create · S-6.2b Return create wizard

```text
+--------------------------------------+
| < Create Return                      |
| Select item(s) [☑ Item 1] [ Item 2]  |
| Reason [v]                            |
| Upload photo [ + ] [ + ] [ + ]        |
| [Idempotency-Key: <auto>]            |
| [            Submit Return          ] |
+--------------------------------------+
```

### #phase-6-return-detail · S-6.3 Return detail

```text
+--------------------------------------+
| < Return #R1001                      |
| Status timeline                       |
| Items + refund amount                 |
| Attachments                           |
+--------------------------------------+
```

### #phase-6-invoice-preview · S-6.4 Invoice preview

```text
+--------------------------------------+
| < Invoice Preview                    |
| Invoice #INV-2026-04-000123          |
| Tax: 15% VAT                         |
| [ PDF preview canvas ]                |
| [          Download PDF             ] |
+--------------------------------------+
```

### #phase-6-invoice-pdf · S-6.5 Invoice PDF download

```text
+--------------------------------------+
| < Invoice PDF                        |
| File ready: invoice-123456.pdf        |
| [Open] [Share] [Download again]       |
+--------------------------------------+
```

---

## Phase 7 — Trust & Compliance (Reviews + Verification)

### #phase-7-verification-list · S-7.1 Verification list

```text
+--------------------------------------+
| < Verification                       |
| Active verification card              |
|  Status: Approved · Expires 2027-04  |
| Previous requests list                |
| [Start New] [Resume]                  |
+--------------------------------------+
```

### #phase-7-verification-submit · S-7.2 Submit verification

```text
+--------------------------------------+
| < Submit Verification                |
| Dynamic fields (from /schema)         |
| Identity / business details           |
| Document slots [+] [+]                |
| [            Submit Request         ] |
+--------------------------------------+
```

### #phase-7-verification-detail · S-7.3 Verification detail + docs upload

```text
+--------------------------------------+
| < Verification Detail                |
| Case status + timeline                |
| Requested info / documents            |
| [Upload Docs] [Resubmit]              |
+--------------------------------------+
```

### #phase-7-verification-resubmit · S-7.4 Resubmit / renew

```text
+--------------------------------------+
| < Resubmit                           |
| Requested fixes checklist             |
| Updated fields                        |
| [             Resubmit              ] |
+--------------------------------------+
```

### #phase-7-review-submit · S-7.5 Submit review

```text
+--------------------------------------+
| < Write Review                       |
| Stars: ★★★★★                          |
| Comment [_________________________]   |
| Add media [ + ]                       |
| [Idempotency-Key: <auto>]            |
| [             Submit Review         ] |
+--------------------------------------+
```

### #phase-7-my-reviews · S-7.6 My reviews list

```text
+--------------------------------------+
| < My Reviews                         |
| Review card #1 · Visible              |
| Review card #2 · Pending moderation   |
+--------------------------------------+
```

### #phase-7-review-detail · S-7.7 My review detail / edit

```text
+--------------------------------------+
| < Review Detail                      |
| Rating + text + media                 |
| Moderation status                     |
| [Edit] [Report]                       |
+--------------------------------------+
```

### #phase-7-report · S-7.8 Report review

```text
+--------------------------------------+
| < Report Review                      |
| Reason ( ) Spam ( ) Abuse             |
| Note [___________________________]    |
| [               Report              ] |
+--------------------------------------+
```

---

## Phase 8 — B2B

### #phase-8-quotes-list · S-8.1 My quotes

```text
+--------------------------------------+
| < My Quotes                          |
| [All][Awaiting][Accepted]             |
| Quote #Q-001 · Draft                  |
| Quote #Q-002 · Published              |
+--------------------------------------+
```

### #phase-8-quotes-awaiting · S-8.2 Awaiting my approval

```text
+--------------------------------------+
| < Awaiting My Approval               |
| Approval item #1                      |
| Approval item #2                      |
+--------------------------------------+
```

### #phase-8-quote-from-cart · S-8.3 Quote from cart

```text
+--------------------------------------+
| < Request Quote (Cart)               |
| Cart summary                          |
| Terms / expected qty                  |
| [Idempotency-Key: <auto>]            |
| [            Submit RFQ             ] |
+--------------------------------------+
```

### #phase-8-quote-from-product · S-8.4 Quote from product

```text
+--------------------------------------+
| < Request Quote (Product)            |
| Product snapshot                      |
| Qty / terms                           |
| [Idempotency-Key: <auto>]            |
| [            Submit RFQ             ] |
+--------------------------------------+
```

### #phase-8-quote-detail · S-8.5 Quote detail + actions

```text
+--------------------------------------+
| < Quote Detail                       |
| Version timeline                       |
| Pricing table                          |
|--------------------------------------|
| Actions                               |
| [Withdraw] [Request Revision]         |
| [Submit Acceptance] [Finalize]        |
| [Reject Acceptance] [Save Template]   |
+--------------------------------------+
```

### #phase-8-quote-document · S-8.6 Quote document download

```text
+--------------------------------------+
| < Quote Document                      |
| Version [v]   Locale [v]              |
| [            Download PDF           ] |
+--------------------------------------+
```

### #phase-8-company-register · S-8.7 Company registration

```text
+--------------------------------------+
| < Register Company                    |
| Name / VAT / Address fields           |
| [             Create Company         ] |
+--------------------------------------+
```

### #phase-8-company-profile · S-8.8 Company profile

```text
+--------------------------------------+
| < Company Profile                     |
| Profile details editable               |
| [               Save                ] |
+--------------------------------------+
```

### #phase-8-branches · S-8.9 Branches

```text
+--------------------------------------+
| < Branches                            |
| Branch list                            |
| [Add Branch]                           |
| Per row: [Delete]                      |
+--------------------------------------+
```

### #phase-8-invite-user · S-8.10 Invite user

```text
+--------------------------------------+
| < Invite User                         |
| Email   [_______________________]     |
| Role    [Buyer ▾]                     |
| [             Send Invite          ]  |
+--------------------------------------+
```

### #phase-8-invitations · S-8.11 Invitations (accept/decline deep link)

```text
+--------------------------------------+
| < Invitations                          |
| Company X invites you as Buyer        |
| [Accept]  [Decline]                   |
+--------------------------------------+
```

### #phase-8-memberships · S-8.12 Memberships

```text
+--------------------------------------+
| < Memberships                          |
| Ahmed   Buyer    [Role ▾] [Remove]     |
| Sara    Approver [Role ▾] [Remove]     |
+--------------------------------------+
```

### #phase-8-legacy-quotations · S-8.legacy.1/2 Legacy quotations

```text
+--------------------------------------+
| < Quotations                         |
| Quote #Q101  Pending                 |
| Quote #Q102  Expired                 |
+--------------------------------------+

+--------------------------------------+
| < Quote #Q101                        |
| Line items + totals                   |
| Terms and validity                    |
| [Accept] [Reject]                     |
+--------------------------------------+
```
