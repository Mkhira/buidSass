---
name: impeccable-brand
description: Dental Commerce Platform brand-adapter overlay for the impeccable design skills. Encodes the locked palette (Principle 7), Arabic/RTL editorial rules (Principle 4), and medical-marketplace tone. Load this WHENEVER invoking any impeccable skill (/impeccable, /audit, /polish, /critique, /typeset, /colorize, /layout, /harden, /adapt, /animate, /delight, /quieter, /bolder, /distill, /clarify, /shape, /optimize, /overdrive). Its rules take precedence over upstream impeccable defaults.
license: Project-internal. Governs Apache-2.0 impeccable skills vendored at commit 00d485659af82982aef0328d0419c49a2716d123. Single committed source of truth: `.claude/skills/impeccable*/`; Codex and GLM access it via `.codex/system.md` and `GLM_CONTEXT.md`.
user-invocable: false
---

# Brand-adapter overlay for impeccable

This overlay is authoritative for every UI or design decision on this repo. It is derived from `.specify/memory/constitution.md` v1.0.0 and MUST NOT be edited without the Principle 32 amendment path.

## Precedence

1. `.specify/memory/constitution.md` (Constitution)
2. This overlay
3. `packages/design_system/tokens.css` and the design-system Dart package
4. Upstream impeccable skills

**Where this overlay and upstream impeccable disagree, this overlay wins. Where this overlay and the Constitution disagree, the Constitution wins.** (Principle 31.)

## Palette — locked (Constitution Principle 7)

| Role      | Hex       | Use                                                        |
|-----------|-----------|------------------------------------------------------------|
| Primary   | `#1F6F5F` | Brand identity, primary CTAs, header anchors               |
| Secondary | `#2FA084` | Supporting actions, links, active states                   |
| Accent    | `#6FCF97` | Highlight, success affinity, non-critical emphasis         |
| Neutral   | `#EEEEEE` | Backgrounds, dividers, surface fills                       |

- Semantic colors (success / warning / error / info) MAY be added **only** for accessibility and medical-marketplace semantics; every addition must trace back to `packages/design_system/tokens.css`.
- No ad-hoc hue may be introduced by an agent. If a design need is unmet, escalate to a human and open a design-system PR.
- Impeccable rules that discourage specific brand hues (e.g. saturated greens) are **overridden** by this table.

## Typography — deferred to tokens

The authoritative font stacks are whatever `packages/design_system/tokens.css` publishes for `--font-ar-*` and `--font-en-*` roles. The overlay does not hardcode families.

- Impeccable's "don't use Arial / don't use Inter" anti-pattern is honored **unless** the design-system token for a role IS that family. The token wins.
- Arabic type MUST be editorial-grade (Principle 4). Never substitute a Latin-optimized family for Arabic text.
- Type scale, line-height, and weight tokens live in the design system. Agents consume them; they do not invent them.

## Arabic & RTL — editorial (Constitution Principle 4)

- Every screen, email, PDF, notification, invoice, and legal page MUST render equivalently in Arabic and English, with full RTL layout mirroring.
- Arabic strings are editorial-grade — never machine-translated. Flag new copy with `needs-ar-editorial-review: true` in the spec file.
- Numeric, date, and currency formatting MUST be locale-aware and market-aware (EG vs KSA); never hardcode `en-US`, `USD`, or Gregorian-only date pickers.
- Icons, arrows, and progress indicators MUST mirror in RTL unless they represent a universal direction (play, clock, etc.).
- Impeccable suggestions that assume a single LTR reading order are **superseded** when Arabic is a target locale.

## Medical-marketplace tone (Principles 8, 9, 27)

- Restricted-product flows (verification-required items) MUST read clinically. No playful microcopy, no casual emoji. Convey **trust, verification state, and consequence of misuse**.
- B2B flows (quotes, company accounts, approver workflows) MUST read efficiently. Dense tables, clear actions, minimal decoration.
- Consumer flows MAY be warmer, but stay within the marketplace-grade register — premium and competent, never whimsical.
- Error and empty states MUST be specific and actionable (Principle 27). "Something went wrong" is non-compliant.

## States (Principle 27)

Every feature spec UI delivery MUST ship: loading, empty, error, success, restricted-state messaging, payment-failure recovery, and accessibility considerations. Impeccable's `/audit` and `/harden` skills are the expected enforcement mechanism.

## Multi-vendor-ready restraint (Principle 6)

The UI ships single-vendor. Do **not** hardcode single-vendor copy (e.g. "our warehouse", "our team") into reusable components. Use neutral phrasing that survives marketplace expansion.

## How this overlay is invoked

- The session-init script prepends this skill to any agent session whose spec number is in {014–024, 029} or whose modified paths include `apps/customer_flutter/**`, `apps/admin_web/**`, or `packages/design_system/**`.
- When the model-invocable `impeccable` skill is loaded, this overlay MUST be loaded alongside it. The session-init script enforces that pairing; a human reviewer is the backstop.
