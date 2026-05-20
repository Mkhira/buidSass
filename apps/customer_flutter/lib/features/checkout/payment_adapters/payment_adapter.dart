import 'package:flutter/widgets.dart';

import '../data/models/checkout_models.dart';

/// Result of a payment-method collection step. Adapters never expose raw
/// PAN / CVV / track data — only a provider-issued token (BR-1 PCI scope
/// **SAQ-A** per ADR-007).
@immutable
class PaymentTokenResult {
  const PaymentTokenResult({
    required this.method,
    this.providerToken,
    this.bankTransferReference,
    this.cancelled = false,
  });

  /// Wire method id — must match `availableMethods` from summary
  /// (server-driven per BR-5).
  final String method;

  /// Opaque token from the provider SDK / hosted fields. Null for
  /// offline methods (cod / bank_transfer where no token is needed).
  final String? providerToken;

  /// Optional customer-supplied reference for bank-transfer flows
  /// (Phase 4 spec.md S-4.7 BR-7).
  final String? bankTransferReference;

  /// True when the user cancelled the adapter mid-flow (e.g. closed the
  /// WebView). The bloc returns to the payment-method picker without
  /// emitting a failure state.
  final bool cancelled;
}

/// Each payment method has an adapter that knows how to collect a
/// provider token (or confirm the user's intent for offline methods).
/// The orchestration `CheckoutPaymentBloc` dispatches to the adapter
/// matching `summary.payment.method` (BR-5) and never reaches into
/// provider SDKs directly.
abstract class PaymentAdapter {
  /// The wire `method` value this adapter handles.
  String get method;

  Future<PaymentTokenResult> collectToken({
    required CheckoutSummary summary,
    required BuildContext context,
  });
}
