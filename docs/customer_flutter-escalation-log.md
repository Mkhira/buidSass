# customer_flutter — Phase 1B contract escalation log

> Per spec 014 FR-031: every backend gap discovered while implementing the
> customer app shell is escalated to the owning Phase 1B spec, never patched
> in this PR. This file is the artifact of that policy.
>
> An empty log on merge is acceptable; the file's absence fails the PR.

## Log

| Date | Owning spec | Gap title | GitHub issue | In-app workaround |
|---|---|---|---|---|
| 2026-04-27 | spec 004 (identity) | Generated dart-dio client not yet imported into `lib/generated/api/` | _TBD_ | `StubAuthRepository` throws `IdentityGapException`; UI surfaces error state via `LoginFailure(reason)`. |
| 2026-04-27 | spec 005 (catalog) | Listing + detail OpenAPI clients not yet wired | _TBD_ | `StubCatalogRepository` returns empty page / `CatalogGapException`. |
| 2026-04-27 | spec 009 (cart) | Cart claim endpoint shape not finalised | _TBD_ | `CartBloc` consumes `CartClaimOutcome.gap` and falls back to `CartMergeService` per FR-013b. |
| 2026-04-27 | spec 010 (checkout) | Drift response schema TBD | _TBD_ | `CheckoutBloc` surfaces `CheckoutDriftException(details)` as `CheckoutDriftBlocked` state; user can re-enter session. |
| 2026-04-27 | spec 011 (orders) | List + detail clients not yet wired | _TBD_ | `StubOrdersRepository` returns empty list / `OrdersGapException`. |
| 2026-04-27 | spec 003 (audit) | `customer_e2e` seed mode doesn't exist on `main` | _TBD_ | T081 integration test deferred until seeder ships. |
| 2026-04-27 | spec 020 (verification) | Not yet shipped | _TBD_ | `VerificationCtaScreen` body gated by `FeatureFlags.verificationCtaShipped` — disabled CTA + placeholder body until 020. |
| 2026-04-27 | spec 022 (CMS) | Not yet shipped | _TBD_ | `CmsStubRepository` serves curated banners + category tiles + empty featured list. |
| 2026-04-27 | spec 023 (notifications) | Live order push deferred per Q3 | _TBD_ | OrderDetailBloc uses pull-to-refresh + open-time fetch only. |

## Closing rules

When a gap is closed:
1. Replace the stub adapter with the generated client in `core/api/api_module.dart` DI bindings.
2. Move the row from "Log" to "Closed" with the merge SHA.
3. Add a regression test that exercises the previously-stubbed surface.

## Closed

| Date | Owning spec | Gap title | Resolution PR |
|---|---|---|---|
