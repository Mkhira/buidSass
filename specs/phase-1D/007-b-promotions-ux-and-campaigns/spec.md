# Feature Specification: Promotions UX & Campaigns

**Feature Branch**: `phase_1D_creating_specs` (working) · target merge branch: `007-b-promotions-ux-and-campaigns`
**Created**: 2026-04-28
**Status**: Draft
**Constitution**: v1.0.0
**Phase**: 1D (Business Modules · Milestone 7)
**Depends on**: 007-a `pricing-and-tax-engine` at DoD; 016 `admin-catalog` contract merged to `main`
**Soft-couples to**: 021 `quotes-and-b2b` (company-scoped business pricing); 024 `cms` (banner-linked campaigns); 019 `admin-customers` (customer / segment pickers); 015 `admin-foundation` (shell, RBAC, audit panel)
**Consumed by**: 005 / 009 / 010 (storefront price hydration) — only via the existing 007-a engine; this spec adds no new pricing primitives
**Input**: User description: "Phase 1D, spec 007-b — promotions UX and campaigns. Coupon admin UX (create, schedule, usage caps, eligibility, deactivate); scheduled promotion authoring (start / end, target, stacking behavior); banner-linked campaigns (CMS 024 integration for hero slots); business-pricing authoring (per-company, per-tier); tier-pricing table editor; preview tool showing resolved price for a sample customer + cart; audit on every promo / coupon / business-pricing edit; promotions-v1 seeder. Bilingual AR + EN end-to-end; multi-vendor-ready."

## Clarifications

### Session 2026-04-28

- Q: How does this spec relate to the 007-a engine — does it introduce new pricing math? → A: **No new pricing math.** 007-b is the authoring / lifecycle / preview surface on top of 007-a's existing tables (`pricing.coupons`, `pricing.promotions`, `pricing.b2b_tiers`, `pricing.product_tier_prices`, `pricing.tax_rates`). All resolution still goes through `IPriceCalculator.Calculate(ctx)`. 007-b owns the admin UX, schedule activation timers, audit + diff capture, banner linkage, preview pipeline, and seeders. Any change that would alter resolved prices is a 007-a amendment, not a 007-b change.
- Q: Coupon code uniqueness scope — global, per-market, or per-campaign? → A: **Global, case-insensitive, canonicalized to uppercase** (consistent with 007-a FR-015). The authoring UI MUST surface a real-time uniqueness check on the code field. Two markets cannot share the same code; if a code is needed in both EG and KSA, the operator creates two separate coupon rows with distinct codes (e.g., `WELCOME10EG`, `WELCOME10SA`) or one row with `markets[]=[EG,SA]`. Recommendation: prefer the multi-market single-row pattern.
- Q: Stacking behavior — what controls do operators get? → A: **Per-rule stacking flags** consistent with 007-a's fixed layer order (list → tier → promotion → coupon → tax). Each promotion exposes `stacks_with_other_promotions: bool` (default `false`); each coupon exposes `stacks_with_promotions: bool` (default `true`) and `stacks_with_other_coupons: bool` (default `false`, hard-enforced by 007-a `409 pricing.coupon.already_applied`). The authoring UI MUST visualize the resulting stacking matrix for a sample cart in the preview tool before save.
- Q: Schedule activation / deactivation — soft-launch with a queued state, or instant? → A: **Three-state lifecycle** for promotions, coupons, and campaigns: `draft` → `scheduled` → `active` → `expired` (or `deactivated` from any non-`expired` state). `scheduled` is a publish-with-future-`valid_from`; the engine treats `valid_from > now` as inactive even if the row is published. A platform timer (no manual cron job) flips `scheduled` → `active` exactly at `valid_from` for status display; the resolution decision is always `now BETWEEN valid_from AND valid_to`. `deactivated` is reversible until `valid_to` passes; `expired` is terminal. Audit captures every transition with the actor and a reason note.
- Q: Banner-linked campaigns — does this spec own the banner CMS, or only the linkage? → A: **Linkage only.** Banner authoring (image upload, slot scheduling, locale variants, market targeting) lives entirely in spec 024 `cms`. 007-b adds a typed `campaign_link` association on the Campaign entity that targets either a `Promotion`, a `Coupon` (display-only — coupon still requires the customer to enter the code), or a `landing_query` (faceted search results for a SKU set). The CMS banner editor in 024 MUST consume a 007-b lookup endpoint to render this picker; if 024 is not yet merged, banner linkage degrades to a free-text "campaign_label" field and the lookup is N/A — no implementation block on 007-b.
- Q: High-impact approval gate default at V1 launch — gate ON, OFF, or unconfigured? → A: **Gate ON by default with conservative seeded thresholds per market.** Each market ships with: `threshold_percent_off = 30`, `threshold_amount_off_minor = SAR 50 000 = 5 000 000 fils` for KSA and `EGP 250 000 = 25 000 000 piasters` for EG, `threshold_duration_days = 14`, and an "either both usage limits unset" rule. Any draft coupon or promotion meeting any one criterion requires a `commercial.approver` co-sign before activation. `super_admin` MAY tune the four threshold fields per market post-launch via the spec 015 admin-foundation settings surface; tunings are audited per Principle 25. Tuning a threshold to `null` disables that single criterion (not the whole gate); disabling the entire gate requires a separate `gate_enabled: bool` market flag (default `true`) that only `super_admin` may flip and that itself is audited.
- Q: Preview profile sharing scope — personal-only, shared-only, or both? → A: **Both, gated promotion.** Every PreviewProfile row carries `visibility ∈ {personal, shared}` and a `created_by` actor pointer; default on create is `personal`. Personal profiles are visible only to their author and to `super_admin`. Promoting to `shared` requires `commercial.approver` or `super_admin`; the promotion is recorded in the audit log with the promoting actor and timestamp. Shared profiles are read-only to non-approvers (including their original author after promotion). Any operator may pick any profile they can see in the Preview drawer. Demotion from `shared` back to `personal` is `super_admin`-only.
- Q: Hard-delete policy for terminal coupons / promotions / campaigns — soft-only or admin purgeable? → A: **Soft-only forever.** Coupons, promotions, and campaigns are NEVER hard-deleted from the operational store regardless of role (including `super_admin`). `expired` and `deactivated` rows remain read-only and queryable indefinitely so that historical `PriceExplanation` rows (007-a `pricing.price_explanations`), audit-trail entries (Principle 25), and order timelines stay resolvable forever. Operator ergonomics for "starting fresh" are served by the Clone-as-draft path (FR-005). Future archival of very old rows to a cold-storage union view MAY be added in a Phase 1.5 retention spec; it is out of scope here. This rule applies to the four lifecycled entities; it does NOT apply to PreviewProfile rows (which the author may delete) nor to BusinessPricingRow / B2BTier rows (whose lifecycle is `active` ↔ `deactivated` and which are also soft-only when referenced by any historical PriceExplanation).
- Q: Coupon-deactivation effect on in-flight carts — strict, in-flight grace, or fully honored? → A: **In-flight grace, market-configurable, default 30 minutes.** A `PriceExplanation` issued before a coupon's deactivation timestamp remains honored for that cart until either (a) the cart reaches `payment_authorized` (state owned by spec 010) or (b) a per-market grace window elapses (default 30 minutes; tunable per market by `super_admin` via spec 015 settings; range 5–120 minutes), whichever comes first. After grace, the engine returns `pricing.coupon.deactivated` on the next price-cart call and the cart is re-priced without the coupon. The same rule applies to a deactivated promotion that was layered into the original explanation. The 007-b deactivation event MUST emit `coupon.deactivation_grace_seconds` and `promotion.deactivation_grace_seconds` so spec 010 / 009 can implement the re-validation gate with the right window. This is a 007-b lifecycle contract; the actual gate code lives in 010. The grace window does NOT apply to `expired` rows — schedule expiry takes effect immediately and the engine rejects on the next call (consistent with 007-a `pricing.coupon.expired`).
- Q: Cascade behavior when an upstream SKU (spec 005) or company (spec 021) is removed — upstream-enforced, downstream-tolerant, or deferred? → A: **Upstream-enforced.** Spec 005 catalog MUST NOT hard-delete a SKU referenced by any 007-b row (Coupon `applies_to`, Promotion `applies_to`, BusinessPricingRow `sku`, Bundle component) whose state is `draft`, `scheduled`, `active`, or `deactivated`; it MUST archive (soft-deactivate) the SKU instead and emit a `catalog.sku.archived` event. Spec 021 quotes-and-b2b MUST NOT hard-delete a company referenced by any 007-b BusinessPricingRow regardless of state; it MUST suspend the company instead and emit a `b2b.company.suspended` event. On receipt of either event, 007-b MUST mark the affected rows with the appropriate broken-reference indicator (`applies_to_broken=true` for SKU events, `company_link_broken=true` for company events), surface them in the operator queue, and gate them: an `active` row whose only references are broken MUST auto-deactivate after a 7-day grace window if no operator action is taken; the auto-deactivation reason note is `"auto_deactivated:broken_references"` and is audited. The engine continues to resolve the rule normally during the grace window (tail risk accepted) but the affected lines emit `pricing.target.archived` warnings in the explanation so support and finance can trace any disputes.

---

## Primary outcomes

1. Commercial operators can author every customer-facing pricing lever — coupons, scheduled promotions, business-account pricing, tier-pricing tables, and banner-linked campaigns — through a single, bilingual admin surface, without engineering involvement and without ever touching the 007-a engine.
2. Every authored rule is **previewable before save**: the operator picks a sample customer (or a saved sample profile) and a sample cart, and sees the exact resolved `PriceExplanation` the engine would emit, layer by layer, with the candidate rule applied.
3. Every create / update / activate / deactivate / expire transition on a promo, coupon, business-pricing override, tier-pricing row, or campaign writes an immutable audit row (Principle 25) with actor, timestamp, before / after diff, and a required reason note for deactivation.
4. Schedule semantics are deterministic and explainable: `valid_from` and `valid_to` use the market's local timezone for display and a UTC instant for resolution. A future-dated rule appears in the queue as `scheduled` and auto-activates at the boundary; an expired rule is read-only.
5. Business-pricing authoring distinguishes the two B2B models cleanly: **company-scoped overrides** (a price specific to one company account from spec 021) and **tier-scoped overrides** (a price every account on tier N receives). Operators can copy a tier as the starting point for a company override.
6. The data model, contract, and admin role boundaries are designed so a future multi-vendor phase (Phase 2) can layer vendor-scoped promo / coupon authoring on top without rewriting any 007-b screen or table.

---

## Roles and actors

| Role | Permission | What they can do in 007-b |
|---|---|---|
| `commercial.operator` | new in this spec | Author + edit coupons, promotions, banner-linked campaigns; preview; submit for activation. Cannot edit business-pricing or tier-pricing tables. |
| `commercial.b2b_authoring` | new in this spec | All of `commercial.operator` plus author + edit business-account overrides and tier-pricing rows. |
| `commercial.approver` | new in this spec | Required co-signer to flip a row from `draft` / `scheduled` to `active` when the rule's projected discount value exceeds a market-configured threshold (the "high-impact" gate). Cannot author. |
| `super_admin` | spec 015 | Implicit superset of all three above. |
| `support` | spec 023 | Read-only on coupon code + state for ticket-investigation use; no preview tool, no audit-diff body access. |
| `viewer.finance` | spec 015 | Read-only on the entire 007-b surface for reporting. |

The customer-facing surface is unchanged from 007-a. There is no new customer screen in this spec; coupons still apply at cart and checkout via 009 / 010.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — A commercial operator creates a scheduled coupon and previews it before publishing (Priority: P1)

A commercial operator logs into the admin and opens **Promotions → Coupons → New**. They author `RAMADAN10` — 10 % off, capped at SAR 50, valid Apr 30 → May 30, eligible for KSA + EG, excludes restricted products. Before saving, they open the **Preview** drawer, pick a saved sample profile ("KSA consumer, 3-line cart"), and see the engine's resolved 5-layer explanation with the new coupon row added. They click **Schedule**; the row enters `scheduled` state.

**Why this priority**: Coupon authoring is the most-frequent operator workflow and the core P1 deliverable. Without preview the spec fails Principle 27 (operationally clear).

**Independent Test**: Sign in as `commercial.operator`, create a coupon with a future `valid_from`, open preview against a seeded sample profile, save. Verify the coupon row exists with state `scheduled`, `valid_from` is honored, and the preview matched the runtime resolved price (re-priced after activation).

**Acceptance Scenarios**:

1. *Given* an operator on the New Coupon screen, *when* they enter `RAMADAN10` and the code already exists globally, *then* the form surfaces an inline error `coupon.code.duplicate` before submit.
2. *Given* an operator with `commercial.operator` permission, *when* they open Preview with a saved sample profile, *then* the rendered explanation matches what `IPriceCalculator.Calculate(ctx)` returns for that profile + the in-flight (unsaved) coupon merged into the context.
3. *Given* an operator submits the form with `valid_from` in the future, *then* the row is persisted in `scheduled` state and is **not** returned by the engine until `now ≥ valid_from`.
4. *Given* an operator submits the form with both AR and EN labels missing, *then* the form rejects with `coupon.label.required_bilingual` (Principle 4).
5. *Given* the operator submits the form with `valid_to ≤ valid_from`, *then* the form rejects with `coupon.schedule.invalid_window`.

---

### User Story 2 — A commercial operator schedules a percent-off promotion targeting a SKU list with stacking explicitly off (Priority: P1)

The operator authors a "Sterilization Week" promotion: 15 % off any SKU tagged `sterilization`, valid May 1 → May 8, `stacks_with_other_promotions=false`, `stacks_with_coupons=false`. They use the SKU picker (consumed from spec 016 admin-catalog), preview against a sample cart with two qualifying lines plus an active coupon profile, observe the coupon is suppressed by the no-stack flag, save as `scheduled`.

**Why this priority**: Promotions are the second-most-frequent authoring workflow and the most likely to produce surprising stacking interactions. Preview catches these surprises before customers do.

**Independent Test**: Create a promotion with `stacks_with_coupons=false`, run preview against a profile that includes a valid coupon, verify the engine returns `appliedAmount=0` for the coupon layer with reason `pricing.coupon.suppressed_by_promotion_no_stack`.

**Acceptance Scenarios**:

1. *Given* the operator opens the SKU picker, *when* they search by SKU substring or by category, *then* the picker calls the spec 016 catalog endpoint and supports multi-select up to a configurable cap (default 500).
2. *Given* the promotion's `stacks_with_other_promotions=false`, *when* a second promotion is already active for an overlapping SKU set, *then* the form surfaces a non-blocking warning `promotion.overlap.warning` listing the overlapping rule ids; operator must acknowledge to proceed.
3. *Given* a saved promotion in `scheduled`, *when* the operator clicks **Deactivate** before activation, *then* the row moves to `deactivated`, audit captures actor + required reason note, and the timer no longer auto-activates it.
4. *Given* a promotion in `active`, *when* the operator edits a non-pricing field (label, description, banner linkage), *then* the row remains `active` and audit captures the field-level diff.
5. *Given* a promotion in `active`, *when* the operator attempts to edit a pricing field (`type`, `value`, `applies_to`, `valid_from`, `valid_to`), *then* the form rejects with `promotion.locked.active_pricing_field` and instructs the operator to deactivate first.

---

### User Story 3 — A commercial-B2B authoring user maintains the tier-pricing table and a per-company override (Priority: P1)

A `commercial.b2b_authoring` user opens **Promotions → Business pricing → Tier table**. They edit the Tier 2 row for SKU `GLV-NTR-100` to net 88.00 SAR. They then open **Business pricing → Company overrides**, pick "Al-Salam Polyclinic" (company from spec 021), copy from Tier 2 as a starting point, and override `GLV-NTR-100` to 85.00 SAR specifically for this company. Both saves are previewed against the relevant sample profile.

**Why this priority**: B2B pricing is a launch requirement (Principle 9 — B2B is V1, not deferred). Without a usable authoring surface the engine's tier tables are unmanageable in production.

**Independent Test**: Sign in as `commercial.b2b_authoring`, edit one tier-pricing row and one company override, verify both rows persist in `pricing.product_tier_prices` (existing 007-a table) with the correct discriminator (`tier_id` set vs `company_id` set), and the engine resolves the company override ahead of the tier row for that customer.

**Acceptance Scenarios**:

1. *Given* a `commercial.operator` (without `b2b_authoring`), *when* they navigate to **Business pricing**, *then* the page returns `403 commercial.business_pricing.forbidden`.
2. *Given* a `commercial.b2b_authoring` user, *when* they edit a tier row to a price below the SKU's cost-of-goods (if exposed by spec 005), *then* the form surfaces a non-blocking warning `business_pricing.below_cogs.warning` and requires acknowledgement.
3. *Given* a company override exists for `GLV-NTR-100` at 85.00 SAR, *when* an operator on Tier 2 with that company places a cart, *then* the engine's tier layer applies 85.00 SAR (not the tier 2 88.00) and the explanation row records `ruleId=company_override:<id>`.
4. *Given* an operator deletes a company override, *when* the same customer next prices a cart, *then* the tier-pricing row applies and the explanation reflects `ruleId=tier:<id>`.
5. *Given* the company picker, *when* the operator searches, *then* it consumes a spec 021 lookup endpoint with paging and surfaces the company name in both AR and EN.

---

### User Story 4 — A commercial operator links a banner-driven campaign to a promotion (Priority: P2)

The operator authors a campaign called **"Eid Sale 2026"**. The campaign holds bilingual labels, a `valid_from` / `valid_to`, a `landing_query` ("category=hand-instruments"), and a `campaign_link` pointing to the active "Sterilization Week" promotion. The CMS banner editor in spec 024 then attaches an image to a hero slot and selects this campaign from a dropdown that consumes the 007-b lookup. When a customer taps the banner they land on the search results filtered by `landing_query`; if they add qualifying SKUs to cart, the linked promotion applies through the engine.

**Why this priority**: Campaign linkage is core to merchandising but it depends on spec 024 (CMS) shipping concurrently. P2 acknowledges the soft-coupling.

**Independent Test**: Create a campaign with a `campaign_link` to an active promotion. Verify the lookup endpoint returns the campaign and that the campaign's promotion is reachable via the engine through normal cart pricing — the linkage adds no pricing math, only navigational context.

**Acceptance Scenarios**:

1. *Given* the operator selects a `campaign_link` of type `promotion`, *when* the chosen promotion is `expired`, *then* the form rejects with `campaign.link.target_expired`.
2. *Given* the operator selects a `campaign_link` of type `coupon`, *when* the chosen coupon has `display_in_banners=false`, *then* the form rejects with `campaign.link.coupon_not_displayable`.
3. *Given* spec 024 is not yet merged, *when* the operator authors a campaign, *then* the campaign saves successfully with the banner-side fields ungated; the lookup endpoint returns 200 (no consumer yet).
4. *Given* a campaign in `active`, *when* its linked promotion is `deactivated`, *then* the campaign auto-marks `link_broken=true` and surfaces a queue indicator until an operator re-links or deactivates the campaign.

---

### User Story 5 — A commercial approver gates a high-impact rule before activation (Priority: P2)

A `commercial.operator` drafts a coupon with 50 % off, no cap, valid for 14 days. The market's high-impact threshold is configured at "discount cap > 30 % OR projected GMV impact > SAR 50 000". The form blocks activation with `coupon.activation.requires_approval` and routes the draft to the **Approval queue** (any user with `commercial.approver` may decide). The approver opens the row, runs preview against a representative sample profile, leaves a co-sign note, and clicks **Approve & schedule**. The row flips to `scheduled` (or `active` if `valid_from` ≤ now).

**Why this priority**: A safety gate on extreme rules. P2 because the day-1 default threshold can be conservative and tightened later — but the gate primitive must exist at launch.

**Independent Test**: Configure the threshold, draft a rule that exceeds it, verify the operator cannot self-activate. Sign in as `commercial.approver` and approve. Verify both actors appear in the audit trail with role badges.

**Acceptance Scenarios**:

1. *Given* a draft below threshold, *when* the operator clicks **Schedule**, *then* the row activates without approver involvement.
2. *Given* a draft above threshold, *when* the operator clicks **Schedule**, *then* the form rejects with `coupon.activation.requires_approval` and offers a **Send for approval** action.
3. *Given* the same operator (`commercial.operator`) is granted `commercial.approver` later, *when* they self-approve their own draft, *then* the form rejects with `commercial.self_approval.forbidden` (separation of duties).
4. *Given* an approver opens an above-threshold draft, *when* they enter a co-sign note < 10 chars, *then* the form rejects with `commercial.approval.note_too_short`.
5. *Given* an approval is recorded, *when* the audit log is read, *then* both actors (author + approver) appear on the activation row with their roles and timestamps.

---

### User Story 6 — Operators import the `promotions-v1` seeder for staging and local development (Priority: P2)

A developer or QA engineer runs the seeder from the project's seed framework. It creates: 6 coupons spanning every state (draft / scheduled / active / deactivated / expired) and every type (`percent_off`, `amount_off`, `bogo`, `bundle`); 4 promotions across the same states; 3 business-pricing tier rows + 2 company overrides; 3 campaigns with mixed banner-link types. Bilingual labels are editorial-grade (Principle 4).

**Why this priority**: Without realistic seed data the admin surface and engine cannot be exercised end-to-end in staging or local. P2 because the seeder is a prerequisite for every later phase's QA but does not block any single 007-b screen.

**Independent Test**: Run `seed --dataset=promotions-v1 --mode=apply` against a fresh staging DB and verify (a) every state is represented, (b) the engine resolves a representative cart through every layer, (c) AR labels pass the editorial review checklist.

**Acceptance Scenarios**:

1. *Given* a fresh staging DB, *when* the seeder runs, *then* it produces ≥ 1 row in each of `draft`, `scheduled`, `active`, `deactivated`, `expired` states for both coupons and promotions.
2. *Given* the seeder runs twice on the same DB, *then* it is idempotent (no duplicates; row count after run 2 = row count after run 1).
3. *Given* the seeder runs with `--mode=dry-run`, *then* it exits 0 with a planned-changes report and writes nothing.
4. *Given* the seeder fails partway, *then* the partial transaction is rolled back and the DB is unchanged.
5. *Given* an admin opens any seeded row, *then* the AR and EN labels render correctly with no machine-translated artifacts.

---

### User Story 7 — A commercial operator deactivates an active rule with a required reason note (Priority: P3)

The operator notices a coupon is being abused. They open the row, click **Deactivate**, enter a reason ("abuse detected — internal incident #4421"), and confirm. The row flips to `deactivated`; the engine stops accepting it on the next cart pricing call; the audit trail captures the action with the reason verbatim.

**Why this priority**: Operationally important but used rarely; the gate is straightforward. P3 because the lifecycle primitive is shared with the higher-priority stories.

**Independent Test**: Deactivate an `active` rule with a reason ≥ 10 chars, verify state, verify the next cart pricing returns `pricing.coupon.deactivated`, verify the audit row.

**Acceptance Scenarios**:

1. *Given* an active rule, *when* the operator clicks **Deactivate** without entering a reason, *then* the form rejects with `commercial.deactivation.reason_required`.
2. *Given* a deactivated rule with `valid_to` in the future, *when* the operator clicks **Reactivate**, *then* the row flips back to `active` and audit captures the reactivation with a new actor + reason.
3. *Given* a rule that has reached `valid_to` (`expired`), *when* the operator clicks **Reactivate**, *then* the form rejects with `commercial.reactivation.expired_terminal` and prompts to clone-as-draft.

---

### Edge Cases

- A coupon's `valid_to` falls inside an active customer cart's lifetime: the engine evaluates at price-cart time using `nowUtc`; if the cart is priced after `valid_to`, the coupon returns `pricing.coupon.expired` and the cart is re-priced without it. (No grace — schedule expiry is immediate per FR-003a.)
- A coupon (or promotion) is deactivated while a customer is mid-checkout: any `PriceExplanation` issued before the deactivation timestamp is honored until either spec 010's `payment_authorized` state is reached or the in-flight grace window (default 30 minutes per market) elapses, whichever comes first (FR-003a). After the boundary the next price-cart call returns `pricing.coupon.deactivated` / `pricing.promotion.deactivated` and the cart is re-priced without the rule.
- Two operators edit the same rule concurrently: optimistic-concurrency (row version) — the second save returns `409 commercial.row.version_conflict` with the current row body for merge.
- A bundle promotion's component SKU is later **archived** in spec 005 catalog (per FR-034b, hard-delete is forbidden upstream when referenced): the promotion auto-marks `applies_to_broken=true` and surfaces a queue indicator (FR-034a); the engine continues to resolve the rule but emits a `pricing.target.archived` advisory; if no operator resolves the queue entry within 7 days the rule auto-deactivates per FR-034c.
- A company referenced by a BusinessPricingRow is **suspended** in spec 021 (per FR-034b, hard-delete is forbidden upstream regardless of state): the row auto-marks `company_link_broken=true` (FR-034a) and follows the same 7-day auto-deactivation path (FR-034c).
- Timezone DST boundary on `valid_from`: stored as UTC; admin display in market timezone with the offset annotation. No silent timezone normalization.
- Operator attempts to author a coupon with > 1 000 character label or description: form rejects with `commercial.text.too_long` per documented limits.
- Operator pastes thousands of SKUs into the picker: cap-enforced (default 500); over-cap input rejected with `commercial.applies_to.too_many`.
- Currency mismatch on a multi-market promotion: when `kind=amount_off` (or coupon `type=amount_off`), the rule's amount is stored per-market in the `amount_off_minor_per_market` object (e.g., `{"SA": 5000, "EG": 25000}` — integer minor units per market); for `percent_off` a single `value` percent applies across markets.
- Banner-linked campaign survives the deletion of its CMS banner: the linkage row is preserved on the 007-b side; only the `link_broken` indicator surfaces.
- Approver approves a draft, the threshold is then raised so the draft is no longer "high-impact", and the operator deactivates and re-schedules: the second activation path follows the new (lower-friction) threshold; the original approval is preserved in audit but is not re-asserted.
- Customer market-of-record changes (cross-reference spec 020 §Clarifications): scheduled coupons / promotions targeting only the old market remain valid for other customers; nothing in 007-b auto-mutates on a single customer's market change.

---

## Requirements *(mandatory)*

### Functional Requirements

#### Lifecycle and state model (Principle 24)

- **FR-001**: Promotions, coupons, and campaigns MUST share a four-state lifecycle: `draft` → (`scheduled` | `active`) → `deactivated` (reversible) | `expired` (terminal). Business-pricing overrides and tier rows MUST share a two-state lifecycle: `active` ↔ `deactivated` (no scheduling — they take effect immediately on save and remain in effect until removed or replaced).
- **FR-002**: A platform timer MUST flip `scheduled` → `active` exactly at `valid_from` and `active` → `expired` exactly at `valid_to`, with a tolerance ≤ 60 s. The engine's resolution decision is independent of this timer (it always evaluates `now BETWEEN valid_from AND valid_to`); the timer drives **status display** only.
- **FR-003**: Every transition MUST write an audit row with `actor_id`, `role`, `timestamp_utc`, `from_state`, `to_state`, `reason_note?`, and (for edits) a field-level `before / after` diff (Principle 25). Deactivation MUST require a reason note ≥ 10 characters; activation, scheduling, and edits MUST NOT.
- **FR-003a**: A deactivation event for a coupon or promotion MUST carry an `in_flight_grace_seconds` payload (default 1800; per-market tunable by `super_admin` via spec 015 settings; range 300–7200). A `PriceExplanation` issued before the deactivation `timestamp_utc` MUST remain honored for that cart until either spec 010's `payment_authorized` state is reached or `now ≥ timestamp_utc + in_flight_grace_seconds`, whichever comes first; after that boundary the engine MUST return `pricing.coupon.deactivated` (or `pricing.promotion.deactivated`) on the next price-cart call. This grace MUST NOT apply to schedule expiry (`active` → `expired`), which takes effect immediately. The actual re-validation gate that enforces the boundary lives in spec 010 checkout; 007-b owns only the event payload contract.
- **FR-004**: An `active` promotion or coupon MUST reject pricing-field edits (`type`, `value`, `applies_to`, `valid_from`, `valid_to`); operators MUST deactivate first. Non-pricing fields (labels, descriptions, banner linkage) remain editable in `active`.
- **FR-005**: An `expired` row MUST be read-only. The UI MUST offer a **Clone as draft** action that creates a fresh `draft` row with the prior body and a new (empty) schedule.
- **FR-005a**: Coupons, promotions, and campaigns MUST NEVER be hard-deleted from the operational store, regardless of role (including `super_admin`). `expired` and `deactivated` rows are retained indefinitely so that historical `PriceExplanation` records, audit-trail entries (Principle 25), and order timelines remain resolvable. The admin UI MUST NOT expose a "Delete" affordance on these entities; any attempt via the API MUST return `405 commercial.row.delete_forbidden`. The same retention rule applies to BusinessPricingRow / B2BTier rows that are referenced by any historical `PriceExplanation`; an unreferenced tier-table row MAY be hard-deleted only by `commercial.b2b_authoring` while still in `draft` (i.e., before its first save), otherwise it follows the soft-only rule.

#### Coupon authoring

- **FR-006**: Coupon authoring MUST capture: `code` (canonical uppercase, globally unique), `markets[]` (subset of `[EG, SA]`), `type` (`percent_off` | `amount_off`), `value` (single percent value when `type=percent_off`; required), `amount_off_minor_per_market` (object keyed by market code mapping to integer minor units; required when `type=amount_off`), `cap_minor?`, `per_customer_limit?`, `overall_limit?`, `excludes_restricted: bool`, `eligibility_segment_id?` (from spec 019 admin-customers), `valid_from`, `valid_to`, `stacks_with_promotions: bool` (default `true`), `display_in_banners: bool` (default `false`), `label.ar`, `label.en`, `description.ar?`, `description.en?`.
- **FR-007**: Coupon code uniqueness MUST be enforced at write time with a unique index on `UPPER(code)`. The form MUST surface a real-time uniqueness check on field blur with a < 200 ms p95 response.
- **FR-008**: Coupon authoring MUST reject an empty `markets[]`, an invalid market, `valid_to ≤ valid_from`, missing AR or EN label, or `value` ≤ 0.
- **FR-009**: A `commercial.operator` MUST NOT be able to set `per_customer_limit=0` or `overall_limit=0` (these are blocking values that disable the coupon — the **Deactivate** action is the supported way to disable).

#### Promotion authoring

- **FR-010**: Promotion authoring MUST capture: `kind` (`percent_off` | `amount_off` | `bogo` | `bundle`), `value` (per-market for `amount_off`; bundle uses bundle-SKU pricing per 007-a FR-017), `applies_to[]` SKU list (capped, default 500), `markets[]`, `valid_from`, `valid_to`, `priority` (integer; higher wins on overlap), `stacks_with_other_promotions: bool` (default `false`), `stacks_with_coupons: bool` (default `true`), `label.ar`, `label.en`, `description.ar?`, `description.en?`, `banner_eligible: bool` (default `false`).
- **FR-011**: When a draft promotion's `applies_to[]` overlaps an `active` or `scheduled` promotion's `applies_to[]` and the new promotion has `stacks_with_other_promotions=false`, the form MUST surface a non-blocking warning `promotion.overlap.warning` with the overlapping rule ids; operator MUST acknowledge to proceed.
- **FR-012**: BOGO and bundle authoring MUST require an explicit reward-SKU selection (BOGO) or bundle-SKU selection (bundle) with a real-time availability check against the spec 005 catalog.

#### Business-pricing authoring

- **FR-013**: Business-pricing authoring MUST distinguish two row types in `pricing.product_tier_prices`: a tier row (`tier_id` set, `company_id` null) and a company override row (`company_id` set, `tier_id` may be set as the "copied-from" pointer for audit). The engine resolves company-override > tier > list (existing 007-a layer 2).
- **FR-014**: Authoring MUST accept a CSV-style bulk import for tier rows (columns: `tier_code`, `sku`, `net_minor`, `markets`) with a dry-run preview that lists every row's parsed effect and any rejection reason; only after the operator confirms the preview does the write commit.
- **FR-015**: A company override row MUST be unique per `(company_id, sku, market_code)`; a tier row MUST be unique per `(tier_id, sku, market_code)`. The form MUST surface a real-time conflict check.
- **FR-016**: Business-pricing edits MUST be gated by `commercial.b2b_authoring`; `commercial.operator` MUST receive `403 commercial.business_pricing.forbidden`.

#### Campaign authoring and banner linkage

- **FR-017**: Campaign authoring MUST capture: `name.ar`, `name.en`, `valid_from`, `valid_to`, `markets[]`, `landing_query?`, `campaign_link?` (`{kind: 'promotion'|'coupon'|'landing_only', target_id?}`), `notes_internal?`.
- **FR-018**: Selecting `campaign_link.kind = 'coupon'` MUST require the chosen coupon's `display_in_banners=true`.
- **FR-019**: When a linked promotion or coupon is deactivated or expires, the campaign MUST auto-mark `link_broken=true` and surface a queue indicator. The campaign itself MUST NOT auto-deactivate.
- **FR-020**: Spec 024 CMS MUST consume a 007-b lookup endpoint to render the banner-side campaign picker; the lookup MUST return only campaigns where `markets[]` overlaps the banner's market and the campaign is in `scheduled` or `active`.

#### Preview tool (cross-cutting)

- **FR-021**: Every authoring screen MUST expose a **Preview** drawer that runs the in-flight (unsaved) rule against a sample customer + cart context and renders the engine's full `PriceExplanation`. Preview MUST call `IPriceCalculator.Calculate(ctx)` in `Preview` mode (007-a FR-012) so no row is written.
- **FR-022**: Sample profiles MUST be authorable in a separate **Preview profiles** screen (admin-scoped, not customer-visible) and MUST capture `market_code`, `locale`, `account_kind` (`consumer` | `b2b`), `tier_id?`, `verification_state`, `cart_lines[]` (sku + qty + restricted flag), `visibility ∈ {personal, shared}` (default `personal`), `created_by` actor pointer, and `vendor_id?` (nullable in V1 per FR-034 / Principle 6). Personal profiles are visible only to their `created_by` actor and to `super_admin`; shared profiles are visible to all commercial operators and read-only to non-approvers. Promotion from `personal` → `shared` requires `commercial.approver` or `super_admin` and is audited; demotion is `super_admin`-only and is audited.
- **FR-023**: Preview MUST render the layer-by-layer breakdown identically to the spec 007-a admin "Price explanation" tab, plus a delta ribbon showing each line's change from "engine without the in-flight rule" → "engine with the in-flight rule" (deactivation flag flipped).
- **FR-024**: Preview MUST execute in p95 ≤ 200 ms for a 20-line sample cart.

#### Approval gate (high-impact rules)

- **FR-025**: A market-configured threshold MUST gate activation of a coupon or promotion that meets any of: `value ≥ threshold_percent_off` (for `percent_off`), `cap_minor ≥ threshold_amount_off_minor`, `per_customer_limit` unset AND `overall_limit` unset, OR `(valid_to − valid_from) ≥ threshold_duration_days`. The gate ships **enabled** at V1 launch (`gate_enabled = true` per market) with conservative seeded thresholds: `threshold_percent_off = 30`; `threshold_amount_off_minor = 5 000 000` fils (KSA, = SAR 50 000) and `25 000 000` piasters (EG, = EGP 250 000); `threshold_duration_days = 14`. `super_admin` MAY tune any threshold field per market via spec 015 settings (audited per Principle 25); setting a single threshold to `null` disables that one criterion. Disabling the entire gate requires flipping the per-market `gate_enabled` flag (default `true`); only `super_admin` may flip it and the flip itself is audited.
- **FR-026**: A high-impact draft MUST require approval by a user holding `commercial.approver`, distinct from the author (separation of duties). The approver MUST enter a co-sign note ≥ 10 characters.
- **FR-027**: The audit row for an approved activation MUST list both actors (author + approver) with their roles and timestamps.

#### Audit (Principle 25)

- **FR-028**: Every create, update, lifecycle transition, deactivation, reactivation, and approval event on coupons, promotions, business-pricing rows, tier rows, and campaigns MUST emit an audit row with the actor, role, timestamp, and (for updates) a field-level diff. The audit log surface lives in spec 015 admin-foundation; this spec only writes.
- **FR-029**: Audit rows MUST be immutable and MUST NOT be deletable from the admin UI.

#### Bilingual + RTL (Principle 4)

- **FR-030**: Every operator-authored label, description, name, and reason note MUST capture both `ar` and `en` values where the field is customer-visible; admin-only fields (e.g., `notes_internal`) MAY be single-locale at the operator's discretion.
- **FR-031**: The admin UI MUST switch to RTL when the operator's locale is `ar`, including form fields, tables, breadcrumbs, and the preview drawer.

#### Notifications integration (Principle 19)

- **FR-032**: The platform timer's `scheduled` → `active` and `active` → `expired` transitions MUST emit domain events (`promotion.activated`, `promotion.expired`, `coupon.activated`, `coupon.expired`, `campaign.link_broken`) consumed by spec 025 notifications for optional admin digest emails (a campaign-management digest configurable per operator).
- **FR-033**: This spec MUST NOT directly send notifications; it only emits events.

#### Multi-vendor readiness (Principle 6)

- **FR-034**: Every coupon, promotion, business-pricing row, tier row, campaign row, and preview-profile row MUST carry a `vendor_id` column (nullable in V1; populated by single-vendor seed). The admin UI MUST NOT expose vendor scoping in V1 but the column and its index MUST be present so a future multi-vendor phase can layer vendor-scoped authoring without a schema migration.

#### Cross-module referential integrity

- **FR-034a**: 007-b MUST consume two upstream events: `catalog.sku.archived` (from spec 005) and `b2b.company.suspended` (from spec 021). On receipt, 007-b MUST mark every referencing row in `draft` / `scheduled` / `active` / `deactivated` state with the appropriate broken-reference indicator (`applies_to_broken=true` on any Coupon, Promotion, or BusinessPricingRow whose `applies_to[]` (Coupon/Promotion) or `sku` (BusinessPricingRow) references the archived SKU — including bundle promotions whose bundle-SKU or component SKUs are referenced; `company_link_broken=true` on BusinessPricingRow for company events) and surface them in the operator queue with a "needs review" badge.
- **FR-034b**: Spec 005 catalog MUST refuse to hard-delete any SKU referenced by a 007-b row whose state is not `expired`; spec 021 quotes-and-b2b MUST refuse to hard-delete any company referenced by a BusinessPricingRow regardless of state. Both upstream specs MUST instead archive / suspend and emit the corresponding event. (This requirement is mirrored in those specs and is documented here for traceability.)
- **FR-034c**: An `active` Coupon, Promotion, or BusinessPricingRow whose **only** unbroken references are now broken MUST auto-deactivate **7 days** after the broken-reference indicator was raised, unless an operator has resolved the row in the queue first. The auto-deactivation MUST write an audit row with `actor_id = 'system'`, `reason_note = "auto_deactivated:broken_references"`, and the list of broken reference ids. The engine MAY continue to resolve the rule during the 7-day grace; the resulting `PriceExplanation` MUST include a `pricing.target.archived` advisory row per affected line.

#### Operational safeguards

- **FR-035**: All admin write endpoints MUST be rate-limited per `actor_id` to defeat scripted bulk corruption: 30 writes / minute / actor, 600 / hour / actor (overridable per environment). On limit hit, return `429 commercial.rate_limit_exceeded`.
- **FR-036**: All admin lookup endpoints (sku picker, company picker, segment picker, campaign-link picker) MUST require authentication, MUST respect the same RBAC the authoring screens enforce, and MUST cap result-set size at 200 with paging.
- **FR-037**: The seeder MUST be invoked through the project's existing seed framework with `--dataset=promotions-v1`, MUST be idempotent across re-runs, and MUST honor `--mode=dry-run` and `--mode=apply` consistent with the rest of the seed catalog.

### Key Entities

- **Coupon** — code-driven discount. Lifecycle, eligibility, stacking flags, banner-display flag. Persists in 007-a's `pricing.coupons`; this spec adds the lifecycle, audit, and authoring layer.
- **Promotion** — schedule-driven discount targeting a SKU list. Lifecycle, stacking flags, priority, kind (`percent_off`, `amount_off`, `bogo`, `bundle`). Persists in 007-a's `pricing.promotions`.
- **BusinessPricingRow** — a row in 007-a's `pricing.product_tier_prices` with either `tier_id` set (tier row) or `company_id` set (company override).
- **B2BTier** — read from 007-a's `pricing.b2b_tiers`; this spec exposes the tier-table editor over it.
- **Campaign** — bilingual merchandising container with `valid_from`/`valid_to`, optional `landing_query`, optional `campaign_link` to a Promotion or Coupon. New table in this spec.
- **CampaignLink** — typed association between a Campaign and a Promotion / Coupon / `landing_only`. New table in this spec.
- **PreviewProfile** — admin-only saved sample customer + cart context for the preview tool. Carries `visibility` (`personal` | `shared`) and `created_by`; promotion gated on `commercial.approver`. New table in this spec.
- **CommercialApproval** — co-signature record on a high-impact activation. New table in this spec.
- **CommercialAuditEntry** — append-only diff per change. Persisted via spec 003's shared audit log; this spec provides the diff payload only.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A trained commercial operator MUST be able to author and schedule a typical percent-off coupon (single market, 14-day window, 100-SKU eligibility) in under 4 minutes from sign-in to `scheduled`.
- **SC-002**: Preview MUST render the engine's layer-by-layer explanation in p95 ≤ 200 ms for a 20-line sample cart.
- **SC-003**: 100 % of create / update / lifecycle-transition / deactivation / reactivation / approval events on the in-scope entities MUST produce a matching audit row, verified by the audit-coverage script in spec 015.
- **SC-004**: 0 % of `active` rows MAY have `valid_to ≤ valid_from` or missing required bilingual labels, verified by an integrity-scan job.
- **SC-005**: The platform timer MUST flip `scheduled` → `active` and `active` → `expired` within 60 s of the boundary on staging, measured over a 7-day soak.
- **SC-006**: A SKU-picker query against a 50 000-SKU catalog MUST return p95 ≤ 300 ms.
- **SC-007**: AR-locale screen-render correctness (RTL, label completeness, formatting) MUST score 100 % against a representative 30-screen editorial-review checklist (Principle 4).
- **SC-008**: The `promotions-v1` seeder MUST populate ≥ 1 row in each of `draft`, `scheduled`, `active`, `deactivated`, `expired` for both coupons and promotions, plus 3 tier rows + 2 company overrides + 3 campaigns, in under 10 s on a fresh staging DB.
- **SC-009**: A high-impact draft MUST be activatable only via a co-sign by a distinct `commercial.approver`; 0 instances of self-approval permitted, verified by a permission-policy test.
- **SC-010**: A campaign whose linked promotion is deactivated MUST surface `link_broken=true` within 60 s, verified by a synthetic event-loop test.

---

## Assumptions

- The 007-a engine and its tables (`pricing.coupons`, `pricing.promotions`, `pricing.b2b_tiers`, `pricing.product_tier_prices`, `pricing.tax_rates`, `pricing.price_explanations`) are at DoD on `main` before 007-b implementation begins.
- The 016 admin-catalog SKU picker, 019 admin-customers segment picker, and 015 admin-foundation shell + RBAC + audit panel are at DoD on `main` before 007-b implementation begins.
- Spec 021 (quotes-and-b2b) ships a company lookup endpoint on `main` before the company-override authoring screen is exercised in staging; if 021 is not yet on `main`, the company-override screen degrades to a free-text company id with a TODO marker — no implementation block.
- Spec 024 (cms) consumes the campaign-link lookup endpoint when its banner editor is built; if 024 is not on `main`, banner linkage degrades to a `campaign_label` free-text — no implementation block.
- Spec 025 (notifications) consumes the lifecycle-transition events when its admin-digest channel is built; this spec only emits the events.
- Currency-per-market is fixed (EG → EGP, KSA → SAR) and matches 007-a Assumptions; multi-currency is out of scope.
- Single-vendor at V1 (Principle 6); `vendor_id` columns are present and indexed but not exposed in admin UI.
- Operators sign in through the spec 015 admin shell; this spec does not introduce a new auth path.
- High-impact threshold defaults are formally bound by FR-025 (gate ON at launch with seeded conservative values: 30 % / 14 days / SAR 50 000 / EGP 250 000 / either-usage-limit-unset). `super_admin` may tune per market post-launch and may flip the per-market `gate_enabled` flag; both actions are audited.

---

## Out of Scope

- **Engine changes.** Any change to layer order, rounding, or tax computation is a 007-a amendment, not a 007-b deliverable.
- **Personalized dynamic pricing** (e.g., ML-driven per-customer promotions) — Phase 2.
- **Per-warehouse / per-vendor pricing** — Phase 1.5 / Phase 2.
- **Coupon-code generation in bulk** (e.g., 10 000 unique codes for an external campaign) — captured as a Phase 1.5 backlog item; V1 supports single-code-per-coupon authoring.
- **A/B testing of promotions** — Phase 2.
- **Customer-side coupon-wallet UI** (saved coupons in the app) — Phase 1.5.
- **Banner image authoring, slot scheduling, asset upload** — owned entirely by spec 024 cms.
- **Notification template authoring for admin digests** — owned by spec 025.
- **Pre-spend / accrual loyalty mechanics** — Phase 2.
