import 'package:flutter/material.dart';

import '../data/models/checkout_models.dart';
import 'payment_adapter.dart';

/// Stub adapters that mirror the real provider SDK contracts without
/// pulling actual credentials / packages at this stage. Each adapter
/// simulates one beat of the relevant flow:
///
///   * Hosted-fields methods (`card`, `mada`, `meeza`, `stc_pay`) return
///     a deterministic fake token. Real adapters land in Phase 4.5
///     when provider sandbox credentials are available.
///   * Hosted-redirect methods (`apple_pay`, `tabby`, `tamara`, `valu`)
///     pop a confirm dialog as a stand-in for the SDK flow; on confirm
///     return a token, on dismiss return [PaymentTokenResult.cancelled].
///   * Offline methods (`bank_transfer`, `cod`) return no token; the
///     bank-transfer variant captures an optional reference.
///
/// PCI scope **SAQ-A** (ADR-007 / Phase 4 plan.md): no raw PAN / CVV /
/// track data ever leaves the adapter. The `scripts/ci/check-mobile-pci.sh`
/// guard greps for those tokens outside this folder.
class _StubTokenAdapter implements PaymentAdapter {
  const _StubTokenAdapter(this.method);

  @override
  final String method;

  @override
  Future<PaymentTokenResult> collectToken({
    required CheckoutSummary summary,
    required BuildContext context,
  }) async {
    return PaymentTokenResult(
      method: method,
      providerToken: 'stub-token-$method-${summary.sessionId}',
    );
  }
}

class _StubConfirmAdapter implements PaymentAdapter {
  const _StubConfirmAdapter(this.method);

  @override
  final String method;

  @override
  Future<PaymentTokenResult> collectToken({
    required CheckoutSummary summary,
    required BuildContext context,
  }) async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text('Confirm $method'),
        content: Text(
            'Total ${summary.totals.grandTotal} ${summary.totals.currency}'),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(ctx).pop(false),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.of(ctx).pop(true),
            child: const Text('Confirm'),
          ),
        ],
      ),
    );
    if (ok != true) {
      return PaymentTokenResult(method: method, cancelled: true);
    }
    return PaymentTokenResult(
      method: method,
      providerToken: 'stub-token-$method-${summary.sessionId}',
    );
  }
}

class BankTransferAdapter implements PaymentAdapter {
  const BankTransferAdapter();

  @override
  String get method => 'bank_transfer';

  @override
  Future<PaymentTokenResult> collectToken({
    required CheckoutSummary summary,
    required BuildContext context,
  }) async {
    // Bank transfer collects an optional customer-side reference; the
    // server-issued reference + IBAN arrive on submit response (S-4.8
    // BR-7).
    final controller = TextEditingController();
    final ref = await showDialog<String?>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Bank transfer'),
        content: TextField(
          controller: controller,
          decoration: const InputDecoration(labelText: 'Reference (optional)'),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(ctx).pop(null),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.of(ctx).pop(controller.text.trim()),
            child: const Text('Confirm'),
          ),
        ],
      ),
    );
    if (ref == null) {
      return const PaymentTokenResult(method: 'bank_transfer', cancelled: true);
    }
    return PaymentTokenResult(
      method: 'bank_transfer',
      bankTransferReference: ref.isEmpty ? null : ref,
    );
  }
}

class CodAdapter implements PaymentAdapter {
  const CodAdapter();

  @override
  String get method => 'cod';

  @override
  Future<PaymentTokenResult> collectToken({
    required CheckoutSummary summary,
    required BuildContext context,
  }) async {
    // COD has no token and no provider step — the picker selection
    // itself is the user's confirmation.
    return const PaymentTokenResult(method: 'cod');
  }
}

/// Registry returns the adapter for a given wire method. Methods absent
/// from the registry surface as `unsupported` from the orchestration
/// bloc — important so a server adding a new method tomorrow doesn't
/// crash the app.
class PaymentAdapterRegistry {
  PaymentAdapterRegistry()
      : _byMethod = {
          'card': const _StubTokenAdapter('card'),
          'apple_pay': const _StubConfirmAdapter('apple_pay'),
          'mada': const _StubTokenAdapter('mada'),
          'stc_pay': const _StubTokenAdapter('stc_pay'),
          'tabby': const _StubConfirmAdapter('tabby'),
          'tamara': const _StubConfirmAdapter('tamara'),
          'valu': const _StubConfirmAdapter('valu'),
          'meeza': const _StubTokenAdapter('meeza'),
          'bank_transfer': const BankTransferAdapter(),
          'cod': const CodAdapter(),
        };

  final Map<String, PaymentAdapter> _byMethod;

  PaymentAdapter? forMethod(String method) => _byMethod[method];

  Iterable<String> get supportedMethods => _byMethod.keys;
}
