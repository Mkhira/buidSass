# Implementation Plan — Phase 7: Trust & Compliance

## Module layout

```
apps/customer_flutter/lib/features/
├── verification/
│   ├── data/{verification_gateway,verification_gateway_impl}.dart
│   ├── bloc/{verification_list_bloc,verification_submit_bloc,verification_detail_bloc,resubmit_cubit,renew_bloc}.dart
│   ├── screens/{verification_list_screen,verification_submit_screen,verification_detail_screen,resubmit_screen,renew_screen}.dart
│   └── widgets/{schema_field.dart,document_slot.dart,state_pill.dart,requested_info_checklist.dart}
└── reviews/
    ├── data/{reviews_customer_gateway,reviews_customer_gateway_impl}.dart  # customer endpoints (+ extends Phase 2 aggregates gateway)
    ├── bloc/{review_submit_bloc,my_reviews_bloc,my_review_detail_bloc,report_review_bloc}.dart
    ├── screens/{review_submit_screen,my_reviews_screen,my_review_detail_screen,report_review_screen}.dart
    └── widgets/{stars_input.dart,review_card.dart}
```

## Dynamic form rendering (S-7.2)

`VerificationSubmitBloc` renders one widget per field type:

```dart
Widget renderField(SchemaField f, dynamic value, void Function(dynamic) onChanged) {
  switch (f.type) {
    case 'text': return TextField(...);
    case 'number': return TextField(keyboardType: number, ...);
    case 'enum': return DropdownButton(items: f.options, ...);
    case 'date': return DatePicker(...);
    case 'doc': return DocumentSlot(slotKey: f.key, ...);  // document slot, no upload yet
  }
}
```

Schema fields are validated client-side using `validation.regex` and `required` flags. Server is the source of truth on submit; client validation is defensive.

## Build sequence

1. VerificationGateway + ReviewsCustomerGateway (T-7.1, T-7.2).
2. Verification list (T-7.3).
3. Verification submit with dynamic schema (T-7.4).
4. Verification detail + document upload (T-7.5).
5. Resubmit + renew (T-7.6).
6. Review submit (T-7.7).
7. My reviews list + detail (T-7.8, T-7.9).
8. Report review (T-7.10).
9. Tests + exit (T-7.11, T-7.12).

## Risks specific to Phase 7

| # | Risk | Mitigation |
|---|---|---|
| 1 | Verification schema differs per market in unexpected ways. | Render dynamically; never assume specific keys exist. |
| 2 | Document upload fails halfway through multi-slot submission. | Per-slot Bloc state; retry per slot independently. |
| 3 | Review edit window expires mid-edit. | On save, surface 409 with friendly "edit window closed" message. |
| 4 | Verification rejection note is sensitive. | Render with copy-to-clipboard but no telemetry capture of the note text. |

## Definition of Done

See `checklists/dod.md`.
