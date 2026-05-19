# Implementation Plan — Phase 8: B2B

## Module layout

```
apps/customer_flutter/lib/features/
├── b2b/
│   ├── data/
│   │   ├── quotes_gateway.dart            # 13 quote ops
│   │   ├── quotes_gateway_impl.dart
│   │   ├── companies_gateway.dart         # 7 company ops
│   │   ├── companies_gateway_impl.dart
│   │   ├── legacy_quotations_gateway.dart # 4 ops
│   │   ├── legacy_quotations_gateway_impl.dart
│   │   └── models/
│   ├── bloc/
│   │   ├── my_quotes_bloc.dart
│   │   ├── awaiting_approval_bloc.dart
│   │   ├── quote_from_cart_bloc.dart
│   │   ├── quote_from_product_bloc.dart
│   │   ├── quote_detail_bloc.dart
│   │   ├── quote_document_bloc.dart
│   │   ├── company_register_bloc.dart
│   │   ├── company_profile_bloc.dart
│   │   ├── branches_bloc.dart
│   │   ├── invite_user_bloc.dart
│   │   ├── invitation_accept_bloc.dart
│   │   ├── memberships_bloc.dart
│   │   ├── legacy_quotations_list_bloc.dart
│   │   └── legacy_quotation_detail_bloc.dart
│   ├── screens/
│   │   ├── my_quotes_screen.dart
│   │   ├── awaiting_approval_screen.dart
│   │   ├── quote_from_cart_screen.dart
│   │   ├── quote_from_product_screen.dart
│   │   ├── quote_detail_screen.dart
│   │   ├── company_register_screen.dart
│   │   ├── company_profile_screen.dart
│   │   ├── branches_screen.dart
│   │   ├── invite_user_screen.dart
│   │   ├── invitation_accept_screen.dart
│   │   ├── memberships_screen.dart
│   │   ├── legacy_quotations_list_screen.dart
│   │   └── legacy_quotation_detail_screen.dart
│   └── widgets/
│       ├── quote_state_pill.dart
│       ├── quote_version_timeline.dart
│       ├── quote_actions_toolbar.dart
│       ├── member_row.dart
│       └── role_picker.dart
```

## Routing additions

```
/quotes                                                                  → MyQuotesScreen
/quotes/awaiting-approval                                                → AwaitingApprovalScreen
/quotes/from-cart                                                        → QuoteFromCartScreen
/products/{slug}/quote                                                   → QuoteFromProductScreen
/quotes/{id}                                                             → QuoteDetailScreen
/company/register                                                        → CompanyRegisterScreen
/company/{id}                                                            → CompanyProfileScreen
/company/{id}/branches                                                   → BranchesScreen
/company/{id}/invitations/new                                            → InviteUserScreen
/invitations/{token}                                                     → InvitationAcceptScreen (deep link)
/company/{id}/members                                                    → MembershipsScreen
/legacy-quotations                                                       → LegacyQuotationsListScreen
/legacy-quotations/{id}                                                  → LegacyQuotationDetailScreen
```

Deep link scheme registration for `myapp://invitations/{token}` mirrors Phase 1's email-confirm deep link.

## Role gating

`CompanyProfileBloc.state.loaded.company.myRole` is the source of truth for admin / buyer / approver visibility. UI widgets read this directly; no separate "roles" service.

Approver-only routes (`/quotes/awaiting-approval`) are guarded in the router via a redirect that checks `Me.roles`. Non-approvers attempting deep link land on `/quotes` with a toast.

## Build sequence

1. Gateways: quotes, companies, legacy quotations (T-8.1, T-8.2, T-8.3).
2. Widgets: state pill, version timeline, actions toolbar, member row, role picker (T-8.4).
3. My quotes + awaiting approval (T-8.5, T-8.6).
4. Quote from cart + Quote from product (T-8.7, T-8.8).
5. Quote detail with actions (T-8.9).
6. Quote document download (T-8.10).
7. Company register + profile (T-8.11, T-8.12).
8. Branches (T-8.13).
9. Invite user + invitation accept deep link (T-8.14, T-8.15).
10. Memberships (T-8.16).
11. Legacy quotations list + detail (T-8.17, T-8.18).
12. Tests + exit (T-8.19, T-8.20).

## Risks specific to Phase 8

| # | Risk | Mitigation |
|---|---|---|
| 1 | Two-step acceptance UX confuses buyers. | Clear "Step 1 of 2" badge + explicit Submit + Finalize labels; intermediate state visible. |
| 2 | Role drift (an admin demoted while editing) ⇒ 403 on save. | On 403, refresh profile + render read-only mode. |
| 3 | Invitation token expired between email and tap. | 410 handler with "Ask the company to resend invite" message. |
| 4 | Quote document download is large (multi-page PDF). | Stream download with progress; cache to temp dir. |
| 5 | Legacy quotations endpoint may 404 for migrated accounts. | Hide the menu entry if list returns empty 200 or 404. |
| 6 | Many roles + many states ⇒ combinatorial UI bugs. | Bloc tests cover the gating matrix; widget tests cover the most-used role × state combos. |

## Definition of Done

See `checklists/dod.md`.
