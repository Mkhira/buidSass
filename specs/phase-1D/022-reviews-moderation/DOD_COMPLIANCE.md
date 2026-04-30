# Spec 022 — Definition of Done compliance (T144 + T145 + T146)

**Constitution + ADR fingerprint**: `789f39325c0f0e8d7d646fc493718867540f9da41f1eed71c31bf15b53e8fb62`
(computed via `scripts/compute-fingerprint.sh` against the locked v1.0.0 baseline)

**DoD version**: 1.0 (per `docs/dod.md`)

## DoD checklist (T145)

| Item | Status | Evidence |
|---|---|---|
| All Constitution Principles respected | ✓ | Spec's Constitution Check (plan.md §"Constitution Check") passed all 19 gates pre-design + post-design |
| ADR decisions honored | ✓ | EF Core 9 + Postgres 16 + MediatR + vertical-slice (ADR-003/004); KSA-region residency (ADR-010) |
| State machines explicit | ✓ | `ReviewStateMachine` in `Modules/Reviews/Primitives/` + 5-state transition table covered by `ReviewStateMachineTests` (16 scenarios) |
| Audit trail for critical actions | ✓ | 14 audit-event kinds reachable per data-model §5; verified by `AuditCoverageTests` E2E walk |
| Idempotency on state-mutating endpoints | ✓ | Customer endpoints accept `Idempotency-Key` header; report-flow + threshold-transition tested for replay safety |
| RBAC + permission gates | ✓ | `reviews.moderator`, `reviews.policy_admin`, `super_admin` chord enforced in `DecideModerationHandler`; covered by `DecideModerationContractTests` |
| Localization (AR + EN) | △ | Reason-code ICU keys for both locales in `Modules/Reviews/Messages/`; AR strings flagged in `AR_EDITORIAL_REVIEW.md` as DRAFT pending T142 sign-off (launch blocker, not merge blocker) |
| Tests + coverage | ✓ | 224/224 passing across 5 PRs (#47-#51) + this PR; covers unit, integration, contract, concurrency, audit, perf-functional |
| Append-only audit guarantee | ✓ | `BEFORE UPDATE OR DELETE` triggers on 3 audit-detail tables; `AppendOnlyTriggersTests` verifies `SQLSTATE 23000` rejection |
| Hard-delete forbidden (FR-005a) | ✓ | `DELETE /api/admin/reviews/{id}` returns `405 review.row.delete_forbidden`; verified by `DecideModerationContractTests.Hard_delete_method_returns_405_with_delete_forbidden` |
| Optimistic concurrency on writes | ✓ | xmin row-version on `Review` + `ReviewsMarketSchema`; `If-Match` header surfaces 409; tested across UpdateReview + DecideModeration |
| Multi-vendor readiness (Principle 6) | ✓ | `vendor_id` column reserved on every new row, indexed but unused at V1 |
| Market-config tunable knobs (Principle 5) | ✓ | `reviews_market_schemas` row holds every per-market policy; `UpdateMarketSchemaHandler` exposes PATCH with check-constraint range validation |
| Cross-module fallback bindings | ✓ | `NullOrderLineDeliveryEligibilityQuery`, `NullReviewReporterFactsQuery`, `NullRefundedOrderLineLookup`, `NullReviewDomainEventPublisher` all in place; specs 011/004/013/025 swap real impls via `TryAdd` |
| ManyServiceProvidersCreatedWarning suppression | ✓ | Suppressed in `ReviewsDbContext.OnConfiguring` AND `ReviewsModule.AddDbContext` (belt-and-braces); CI grep guard in `ManyServiceProvidersCreatedWarningSuppressionTests` |
| Domain events (FR-038) | ✓ | 8 `INotification` records published post-commit via `IReviewDomainEventPublisher` (MediatR-backed); failures logged + swallowed; verified by `ReviewDomainEventsPublishedTests` |
| Rate limiting | ✓ | `ReviewRateLimiter` token-bucket: 5/h customer (submit/edit/report separate buckets), 60/h moderator; verified by `ReviewRateLimiterTests` + `SubmitReviewContractTests.Rate_limit_exceeded_returns_429_with_spec_reason_code` |

✓ = closed in code; △ = launch-time editorial sign-off pending (T142, not merge blocker)

## Manual smoke checklist (T146)

The `scripts/manual-smoke-022.sh` companion script runs the following curl sequence
against a deployed environment. Per quickstart §13:

1. **Customer submit** — `POST /api/customer/reviews` with valid body → expect `201 Created`, `state=visible`
2. **Customer edit** — `PATCH /api/customer/reviews/{id}` with rating change → expect `200 OK`, `editCount=1`
3. **Customer list-mine** — `GET /api/customer/reviews/me` → expect `200 OK`, items array contains the just-created id
4. **Customer report** — `POST /api/customer/reviews/{otherId}/report` with `personal_attack` reason → expect `201 Created`, `qualified=true|false` per reporter facts
5. **Admin queue** — `GET /api/admin/reviews/queue?state=pending_moderation` → expect `200 OK`, items array
6. **Admin decide** — `POST /api/admin/reviews/{id}/decide` with `to_state=hidden`, `reason_note=...` → expect `200 OK`, `state=hidden`
7. **Public aggregate** — `GET /api/public/reviews/aggregates/{productId}?market_code=SA` → expect `200 OK`, `Cache-Control: public, max-age=60` header, populated histogram

Smoke procedure runs against Staging post-deploy; results logged in PR body at merge time. The script itself is intentionally a runnable artifact (not a CI test) so the operator can paste real bearer tokens + product/order ids.

## Sign-off

- [ ] PR-stack reviewed end-to-end (#47 → #48 → #49 → #50 → #51 → this PR)
- [ ] Constitution+ADR fingerprint verified by reviewer
- [ ] DoD checklist tick-marks above verified
- [ ] Manual smoke completed against Staging post-merge
- [ ] AR editorial sign-off scheduled (T142 — launch blocker, tracked in `Modules/Reviews/Messages/AR_EDITORIAL_REVIEW.md`)
