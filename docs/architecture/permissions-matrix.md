**Permissions Matrix Version**: 1.0.0 | **Date**: 2026-04-19 | **Status**: Ratified

## Role Legend

| Code | Role |
|---|---|
| G | Guest |
| C | Customer |
| P | Professional (verified customer) |
| BB | B2B Buyer |
| BA | B2B Approver |
| BrA | B2B Branch Admin |
| CO | B2B Company Owner |
| AR | Admin Read-only |
| AW | Admin Write |
| AS | Admin Super |

## Cell Encoding

- ✅ Allowed
- ❌ Denied
- ⚠️ `[condition]` Conditionally allowed (see footnotes)

## Identity & Access

| Action | G | C | P | BB | BA | BrA | CO | AR | AW | AS |
|---|---|---|---|---|---|---|---|---|---|---|
| register | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| login | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| view own profile | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| edit own profile | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| view any profile | ❌ | ❌ | ❌ | ❌ | ❌ | ⚠️ [1] | ⚠️ [2] | ✅ | ✅ | ✅ |
| manage roles | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ⚠️ [3] | ✅ |
| manage permissions | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ |

## Catalog

| Action | G | C | P | BB | BA | BrA | CO | AR | AW | AS |
|---|---|---|---|---|---|---|---|---|---|---|
| browse products | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| view restricted product | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| purchase restricted product | ❌ | ❌ | ✅ | ⚠️ [4] | ⚠️ [4] | ⚠️ [4] | ⚠️ [4] | ❌ | ❌ | ❌ |
| create product | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ |
| edit product | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ |
| delete product | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ⚠️ [5] | ✅ |
| manage categories | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ |
| manage brands | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ |

## Inventory

| Action | G | C | P | BB | BA | BrA | CO | AR | AW | AS |
|---|---|---|---|---|---|---|---|---|---|---|
| view stock levels | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ |
| adjust stock | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ |
| view reservations | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ |
| release reservations | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ |
| manage batch/lot | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ |

## Cart & Checkout

| Action | G | C | P | BB | BA | BrA | CO | AR | AW | AS |
|---|---|---|---|---|---|---|---|---|---|---|
| add to cart | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| view cart | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| apply coupon | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| initiate checkout | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| place order | ❌ | ✅ | ✅ | ✅ | ⚠️ [6] | ⚠️ [7] | ✅ | ❌ | ❌ | ❌ |

## Orders

| Action | G | C | P | BB | BA | BrA | CO | AR | AW | AS |
|---|---|---|---|---|---|---|---|---|---|---|
| view own orders | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| view any order | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ |
| update order status | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ |
| cancel order | ❌ | ✅ | ✅ | ✅ | ⚠️ [8] | ⚠️ [8] | ✅ | ❌ | ✅ | ✅ |
| initiate return | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ |
| download invoice | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |

## Pricing & Promotions

| Action | G | C | P | BB | BA | BrA | CO | AR | AW | AS |
|---|---|---|---|---|---|---|---|---|---|---|
| view prices | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| view business pricing | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| create coupon | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ |
| create promotion | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ |
| set tier pricing | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ |
| set business pricing | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ |

## Verification

| Action | G | C | P | BB | BA | BrA | CO | AR | AW | AS |
|---|---|---|---|---|---|---|---|---|---|---|
| submit verification | ❌ | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| view own verification | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| view any verification | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ |
| review verification | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ |
| approve/reject verification | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ |

## Quotes & B2B

| Action | G | C | P | BB | BA | BrA | CO | AR | AW | AS |
|---|---|---|---|---|---|---|---|---|---|---|
| request quote | ❌ | ❌ | ⚠️ [9] | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| view own quotes | ❌ | ❌ | ⚠️ [9] | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| view company quotes | ❌ | ❌ | ❌ | ⚠️ [10] | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| author quote | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ |
| approve quote | ❌ | ❌ | ❌ | ❌ | ✅ | ⚠️ [11] | ✅ | ❌ | ✅ | ✅ |
| convert quote to order | ❌ | ❌ | ❌ | ⚠️ [10] | ✅ | ⚠️ [11] | ✅ | ❌ | ✅ | ✅ |
| manage company members | ❌ | ❌ | ❌ | ❌ | ❌ | ⚠️ [12] | ✅ | ❌ | ❌ | ❌ |

## Reviews, Support, CMS, Notifications

| Action | G | C | P | BB | BA | BrA | CO | AR | AW | AS |
|---|---|---|---|---|---|---|---|---|---|---|
| submit review | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| moderate review | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ |
| create ticket | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| view tickets | ❌ | ⚠️ [13] | ⚠️ [13] | ⚠️ [13] | ⚠️ [13] | ⚠️ [13] | ⚠️ [13] | ✅ | ✅ | ✅ |
| reply to ticket | ❌ | ⚠️ [13] | ⚠️ [13] | ⚠️ [13] | ⚠️ [13] | ⚠️ [13] | ⚠️ [13] | ✅ | ✅ | ✅ |
| publish CMS content | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ |
| manage notification templates | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ |

## Footnotes

- [1] Branch admin can view profiles only for members in assigned branch.
- [2] Company owner can view profiles only for members in owned company.
- [3] Admin Write can assign operational roles but cannot edit Admin Super permissions.
- [4] Restricted purchase allowed only for verified professional entitlement linked to account/company.
- [5] Product deletion allowed only when product has no historical order references.
- [6] B2B Approver can place order only for approved company cart.
- [7] Branch admin can place order only for own branch carts.
- [8] Cancellation allowed only before shipment creation and within cancellation window.
- [9] Professional quote actions require active professional verification.
- [10] B2B Buyer can access or convert only quotes they requested.
- [11] Branch admin can approve/convert only for branch-scoped quotes.
- [12] Branch admin can manage members only within branch-level roles.
- [13] User can view/reply only to tickets they created or that belong to their company context.
