# AR Editorial Review Tracker — Verification (Spec 020)

Tracks Arabic ICU keys in `verification.ar.icu` that are awaiting editorial sign-off
per **Principle 4** (Arabic quality MUST be editorial-grade, not machine-translated).

## Workflow

1. Each phase (Phase 3 onward) adds AR keys to `verification.ar.icu` alongside their
   EN counterparts in `verification.en.icu`.
2. New AR keys are listed in the "Pending review" table below with the slice that
   introduced them, the originating PR, and the engineer who staged the AR copy.
3. The editorial reviewer signs off by checking the row's `Reviewed?` box and
   moving the row into "Approved keys" with a date.
4. SC-008 (DoD) requires every AR key in `verification.ar.icu` to appear in the
   "Approved keys" section before the spec can ship.

## Pending review

| Slice / Phase | Key | Added by | PR | Reviewed? |
|---|---|---|---|---|

_(empty — no AR keys staged yet; first batch lands in Phase 3 / US1)_

## Approved keys

| Key | Reviewed by | Approved date |
|---|---|---|

_(empty)_

## Notes for reviewers

- AR strings render right-to-left; ICU placeholders (`{name}`, `{count, plural, ...}`)
  must preserve their syntax exactly — do not translate the placeholder names.
- Verification copy frequently references regulator-specific terminology (e.g.,
  "Saudi Commission for Health Specialties", "Egyptian Medical Syndicate"); use
  the official Arabic name from each regulator's own publications, not a literal
  translation of the English label.
- For market-aware error messages, KSA-AR and EG-AR may differ slightly in formal
  vs. colloquial register; flag any reviewer concern in the "Reviewed?" column
  with a brief note.
