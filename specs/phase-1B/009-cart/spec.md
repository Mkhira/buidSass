# Feature Specification: Cart (v1)

**Feature Number**: `009-cart`
**Phase Assignment**: Phase 1B · Milestone 3 · Lane A (backend)
**Created**: 2026-04-22
**Input**: constitution Principles 3, 5, 6, 8, 9, 10, 11, 17, 22, 23, 24, 25, 27, 28, 29.

---

## Clarifications

### Session 2026-04-22

- Q1: Anonymous carts? → **A: Yes.** An anonymous cart is identified by an opaque `cart_token` cookie/header; it can be merged into the authenticated cart on login (Principle 3 — browse without auth).
- Q2: Cart scope across markets? → **A: One active cart per (account, market).** Switching market archives the old cart (recoverable for 7 days).
- Q3: Inventory reservation at cart add? → **A: Yes, soft reservation via spec 008** with 15-min TTL. Extends on each touch.
- Q4: Pricing on cart read? → **A: Recalculated each read via spec 007-a `Preview` mode** (no stored totals on the cart). Stored only at checkout (spec 010).
- Q5: B2B fields on cart? → **A: PO number, reference, notes, requested delivery window.** Filled by buyer, preserved across merges.
- Q6: Quantity bounds? → **A: `min_order_qty` and `max_per_order` sourced from catalog product metadata.** Cart validates on add/update.

---

## User Scenarios & Testing

### User Story 1 — Anonymous browse + add (P1)
A logged-out shopper in KSA adds 2 units. Cart is created keyed by cookie. ATS reserved via spec 008.

**Acceptance Scenarios**:
1. *Given* no cart exists, *when* `POST /carts/items` is called with `cart_token=""`, *then* a new cart is created, `cart_token` returned in response and `Set-Cookie`.
2. *Given* an anonymous cart, *when* it is read, *then* pricing is computed via spec 007-a Preview.
3. *Given* add exceeds ATS, *then* response is `409 cart.inventory_insufficient` with `{ shortfallByProduct }`.

---

### User Story 2 — Login merges cart (P1)
An anonymous cart exists; user logs in. The two carts (if both exist) merge by summing line qty subject to `max_per_order`.

**Acceptance Scenarios**:
1. *Given* anon cart has 2 of A, user cart has 1 of A + 3 of B, *when* merge runs, *then* merged cart has 3 of A + 3 of B (assuming max ≥ 3 for A).
2. *Given* sum exceeds `max_per_order`, *then* qty is capped at max and a `cart.merge.qty_capped` notice is returned per line.
3. *Given* inventory reservations exist on both carts, *then* the merged cart consolidates reservations (old reservation refs released + single new reservation created).

---

### User Story 3 — Restricted product interaction (P1)
User adds a restricted product. Cart accepts it (Principle 8 — visibility preserved) and records `restricted=true`; checkout will enforce (spec 010).

**Acceptance Scenarios**:
1. *Given* a restricted product, *when* added to cart, *then* line is added with `restricted=true` and `restrictionReasonCode=catalog.restricted.verification_required`.
2. *Given* an unverified customer views the cart, *then* the cart response includes a top-level `checkoutEligibility = { allowed: false, reasonCode: "catalog.restricted.verification_required" }`.
3. *Given* a verified customer views the same cart, *then* `checkoutEligibility.allowed = true`.

---

### User Story 4 — Quantity update / remove (P1)
User changes qty from 2 → 3. Reservation extends; price recalculates; response includes full breakdown.

**Acceptance Scenarios**:
1. *Given* a line at qty 2, *when* patched to qty 3, *then* reservation extends by 1 unit (FEFO).
2. *Given* a line at qty 3, *when* patched to qty 0, *then* the line is removed and its reservation released.
3. *Given* qty below `min_order_qty`, *then* `400 cart.below_min_qty`.

---

### User Story 5 — B2B cart fields (P2)
A clinic buyer sets PO number `PO-2026-0042`, reference `Invoice-to Dr. Khalid`, requested delivery `2026-04-28`.

**Acceptance Scenarios**:
1. *Given* a B2B account, *when* the user patches cart metadata, *then* fields are persisted and returned on read.
2. *Given* a non-B2B account, *when* B2B fields are submitted, *then* `403 cart.b2b_fields_forbidden`.
3. *Given* the cart is merged (US2), *then* B2B fields from the authenticated cart win.

---

### User Story 6 — Cart abandoned email trigger (P2)
A cart with ≥ 1 line idle for > 1 h emits `cart.abandoned` event. Spec 019 notifications consume it.

**Acceptance Scenarios**:
1. *Given* a cart idle 60 min, *when* the worker ticks, *then* `cart.abandoned` is emitted once per cart per idle period.
2. *Given* the cart resumes, *then* the idle timer resets.
3. *Given* a guest cart with no email, *then* no event is emitted (no target).

---

### User Story 7 — Save for later / move between carts (P2)
User moves a line from active cart to a "save for later" bucket. Reservation is released.

**Acceptance Scenarios**:
1. *Given* an active line, *when* moved to saved, *then* the reservation releases and the item appears in the saved list.
2. *Given* a saved item, *when* moved back to active, *then* a new reservation is attempted; if insufficient, the move fails with `409 cart.inventory_insufficient`.

---

### Edge Cases
1. Cart size cap: 100 distinct lines per cart (defensive bound). `413 cart.too_many_lines`.
2. Adding a product not published in the cart's market → `400 cart.product_market_mismatch`.
3. Price drift between cart read and checkout submit → cart Preview is authoritative at the cart surface; checkout Issue re-prices and any drift surfaces there (not here).
4. Reservation lost between reads (TTL expired) → on next read, cart attempts re-reservation; if insufficient, line is flagged `stockChanged=true` for UI.
5. Anonymous cart merge where anon had a coupon → coupon preserved if still valid for the authenticated account.
6. Guest with no cookie and a cart_token in header → header wins.
7. Currency/market mismatch at merge → reject merge with `409 cart.market_mismatch`; archive anon cart and start fresh.
8. Two tabs racing add operations → optimistic row_version conflict → client retries; worst-case last-write-wins on metadata, per-line adds are additive.
9. Cart recovery within 7 days after market switch → `POST /carts/restore/{archivedCartId}` — available if same account.
10. Very large qty requests → capped at product's `max_per_order` with `cart.line.qty_capped` notice.

---

## Requirements (FR-)
- **FR-001**: System MUST support anonymous carts via opaque `cart_token`.
- **FR-002**: System MUST support authenticated carts keyed by `(account_id, market_code)`; one active cart per pair.
- **FR-003**: System MUST merge anonymous cart into authenticated cart on login.
- **FR-004**: System MUST reserve inventory via spec 008 on add and update; release on remove / abandon.
- **FR-005**: System MUST compute pricing on every cart read via spec 007-a Preview.
- **FR-006**: System MUST expose `checkoutEligibility` top-level flag derived from restriction + inventory + B2B prerequisites.
- **FR-007**: System MUST enforce `min_order_qty` and `max_per_order` from catalog metadata.
- **FR-008**: System MUST support B2B cart fields: `po_number`, `reference`, `notes`, `requested_delivery_window` (B2B accounts only).
- **FR-009**: System MUST archive a cart on market switch; archived carts recoverable for 7 days.
- **FR-010**: System MUST emit `cart.abandoned` event for carts idle ≥ 60 min with at least 1 line AND a known email, once per idle period.
- **FR-011**: System MUST support save-for-later with a separate `cart_saved_items` container; items in saved do not reserve inventory.
- **FR-012**: Cart updates MUST be optimistic-concurrency-safe via `row_version`.
- **FR-013**: Cart responses MUST include full pricing breakdown (per line + totals) from spec 007-a.
- **FR-014**: Restricted products MUST be addable (Principle 8); eligibility gating lives in `checkoutEligibility` + spec 010 enforcement.
- **FR-015**: Coupon apply/remove MUST be supported on the cart: `POST /carts/{id}/coupon` and `DELETE /carts/{id}/coupon`.
- **FR-016**: Cart abandonment detection MUST NOT trigger more than once per cart per 24 h.
- **FR-017**: System MUST expose admin read-only cart inspection for support (audit-logged access).
- **FR-018**: Cart size cap MUST be 100 distinct lines; exceeding returns `413`.
- **FR-019**: Every cart mutation MUST write an audit row if the actor is admin; customer mutations are logged but not audit-level (volume).
- **FR-020**: Cart cookie MUST be HttpOnly, Secure, SameSite=Lax; token lifetime 30 days.
- **FR-021**: Guest carts without activity for 30 days MUST be purged by a cleanup worker.
- **FR-022**: On product archive (spec 005) or market removal, affected cart lines MUST be flagged `unavailable=true` on next read — not silently removed.

### Key Entities
- **Cart** — `(id, account_id?, cart_token?, market_code, status)`.
- **CartLine** — `(cart_id, product_id, qty, reservation_id?, added_at)`.
- **CartSavedItem** — `(cart_id, product_id, saved_at)`.
- **CartB2BMetadata** — `(cart_id, po_number, reference, notes, requested_delivery_window)`.
- **CartEvent** — `cart.abandoned` emission record for dedup.

---

## Success Criteria (SC-)
- **SC-001**: Cart read p95 ≤ 120 ms (includes Preview pricing + inventory bucket batch + DB fetch).
- **SC-002**: Cart add p95 ≤ 150 ms (includes reservation + preview pricing).
- **SC-003**: Anon→auth merge correctness: 100 random merge scenarios → 0 drift in summed qty.
- **SC-004**: Abandonment event emitted exactly once per cart per 24 h idle period (SC verified via fault-injection).
- **SC-005**: Anonymous carts purged after 30 d: asserted by a scheduled-cleanup test.
- **SC-006**: Restricted product appears in cart with `restricted=true` and eligibility gate correctly populated.
- **SC-007**: Reservation consistency: cart line `reservation_id` matches an active spec 008 reservation row at all times (verified by periodic consistency job in tests).
- **SC-008**: Market switch archives the previous cart and allows recovery within 7 days.

---

## Dependencies
- Spec 005 catalog, spec 006 search (for product availability facet), spec 007-a pricing, spec 008 inventory, spec 004 identity/RBAC, spec 003 audit/messages.

## Assumptions
- One active cart per `(account, market)` pair.
- B2B fields only for accounts flagged `is_b2b=true` in spec 004.
- Save-for-later does not reserve inventory.

## Out of Scope
- Multi-cart ("shopping lists") — Phase 1.5.
- Shared team cart editing (B2B multi-user) — Phase 1.5.
- Cross-market cart recovery — Phase 1.5.
- Gift-wrap / gift-message line metadata — Phase 2.
