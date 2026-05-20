import 'package:flutter/foundation.dart';

import '../data/models/checkout_models.dart';

/// Drift-resolution outcomes the screen layer reports back to the bloc.
/// `accept` calls the accept-drift endpoint and re-runs the original
/// PATCH/POST; `review` routes back to summary; `dismiss` cancels the
/// current step without resolving.
enum DriftResolution { accept, review, dismiss }

@immutable
class CheckoutConflict {
  const CheckoutConflict({required this.details, this.correlationId});
  final DriftDetails details;
  final String? correlationId;
}

/// Mixed into each step bloc's run-helper:
/// ```dart
/// try {
///   final s = await _gateway.patchAddress(...);
///   emit(StepLoaded(s));
/// } on CheckoutDriftException catch (e) {
///   emit(StepConflict(CheckoutConflict(details: e.details, correlationId: e.correlationId)));
/// }
/// ```
/// Keeping the helpers small avoids re-inventing the same try/catch in
/// every step bloc.
mixin CheckoutDriftAware {
  CheckoutConflict driftFrom(CheckoutDriftException e) =>
      CheckoutConflict(details: e.details, correlationId: e.correlationId);
}
