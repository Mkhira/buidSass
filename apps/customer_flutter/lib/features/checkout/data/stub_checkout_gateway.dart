import 'checkout_gateway.dart';
import 'models/checkout_models.dart';

/// Deterministic in-memory [CheckoutGateway] for offline dev and tests
/// that exercise the bloc layer without spinning up the backend.
///
/// Behavior:
///   * `createSession` produces a session id derived from input lines.
///   * `getSummary` returns a snapshot kept in `_sessions[sessionId]`.
///   * PATCHes mutate the in-memory summary and advance `stepStatus`.
///   * `submit` always succeeds for non-bank-transfer methods; for
///     `bank_transfer` it returns a fake reference + IBAN so the
///     confirmation screen can render the post-submit branch.
///   * `priceCart` echoes inputs with a 15% tax and 5% discount.
///
/// Drift simulation: pass `simulateDrift: true` to force the next mutation
/// to throw a [CheckoutDriftException] with a single price delta.
class StubCheckoutGateway implements CheckoutGateway {
  StubCheckoutGateway({this.simulateDrift = false});

  bool simulateDrift;
  final Map<String, CheckoutSummary> _sessions = {};

  @override
  Future<CreateSessionResult> createSession(CreateSessionRequest req) async {
    final sessionId = 'stub-${DateTime.now().millisecondsSinceEpoch}';
    final expires = DateTime.now().add(const Duration(minutes: 30));
    final lines = req.lines
        .map((l) => CheckoutSummaryLine(
              productId: l.productId,
              name: 'Product ${l.productId}',
              qty: l.qty,
              unitPrice: '120.00',
              lineTotal: (120 * l.qty).toStringAsFixed(2),
            ))
        .toList(growable: false);
    final summary = CheckoutSummary(
      sessionId: sessionId,
      expiresAt: expires,
      lines: lines,
      address: null,
      shipping: const CheckoutShippingInfo(),
      payment: const CheckoutPaymentInfo(),
      totals: _computeTotals(lines, shippingCost: 0, discount: 0),
      availableMethods: _availableMethodsFor(req.marketCode),
      stepStatus: const CheckoutStepStatusMap(),
    );
    _sessions[sessionId] = summary;
    return CreateSessionResult(
      sessionId: sessionId,
      expiresAt: expires,
      summary: summary,
      availableSteps: const ['address', 'shipping', 'payment', 'review'],
    );
  }

  @override
  Future<CheckoutSummary> getSummary(String sessionId) async {
    return _require(sessionId);
  }

  @override
  Future<List<ShippingQuoteOption>> getShippingQuotes(String sessionId) async {
    _require(sessionId);
    return const [
      ShippingQuoteOption(
        method: 'standard',
        label: 'Standard',
        price: Money(amount: '15.00', currency: 'SAR'),
        etaDays: '2-3',
      ),
      ShippingQuoteOption(
        method: 'express',
        label: 'Express',
        price: Money(amount: '35.00', currency: 'SAR'),
        etaDays: '1',
      ),
    ];
  }

  @override
  Future<CheckoutSummary> patchAddress({
    required String sessionId,
    required CheckoutAddressDto address,
  }) async {
    _maybeDrift();
    final s = _require(sessionId);
    final next = _copy(
      s,
      address: address,
      stepStatus: CheckoutStepStatusMap(
        address: CheckoutStepStatus.complete,
        shipping: s.stepStatus.shipping,
        payment: s.stepStatus.payment,
        review: s.stepStatus.review,
      ),
    );
    _sessions[sessionId] = next;
    return next;
  }

  @override
  Future<CheckoutSummary> patchShipping({
    required String sessionId,
    required String method,
  }) async {
    _maybeDrift();
    final s = _require(sessionId);
    final cost = method == 'express' ? 35.0 : 15.0;
    final next = _copy(
      s,
      shipping: CheckoutShippingInfo(
        method: method,
        cost: Money(amount: cost.toStringAsFixed(2), currency: 'SAR'),
        etaDays: method == 'express' ? '1' : '2-3',
      ),
      totals: _computeTotals(s.lines, shippingCost: cost, discount: 0),
      stepStatus: CheckoutStepStatusMap(
        address: s.stepStatus.address,
        shipping: CheckoutStepStatus.complete,
        payment: s.stepStatus.payment,
        review: s.stepStatus.review,
      ),
    );
    _sessions[sessionId] = next;
    return next;
  }

  @override
  Future<CheckoutSummary> patchPaymentMethod({
    required String sessionId,
    required String method,
    String? providerToken,
    String? bankTransferReference,
  }) async {
    _maybeDrift();
    final s = _require(sessionId);
    final next = _copy(
      s,
      payment: CheckoutPaymentInfo(method: method),
      stepStatus: CheckoutStepStatusMap(
        address: s.stepStatus.address,
        shipping: s.stepStatus.shipping,
        payment: CheckoutStepStatus.complete,
        review: s.stepStatus.review,
      ),
    );
    _sessions[sessionId] = next;
    return next;
  }

  @override
  Future<SubmitResult> submit({
    required String sessionId,
    required String idempotencyKey,
  }) async {
    _maybeDrift();
    final s = _require(sessionId);
    final method = s.payment.method ?? 'cod';
    return SubmitResult(
      orderId: 'stub-order-$sessionId',
      orderNumber: '2026-05-${sessionId.hashCode.abs() % 1000000}',
      paymentState: method == 'bank_transfer' ? 'pending' : 'captured',
      fulfillmentState: 'pending',
      redirect: const SubmitRedirect(kind: 'none'),
      bankTransfer: method == 'bank_transfer'
          ? BankTransferDetails(
              reference: 'REF-$sessionId',
              iban: 'SA0000000000000000000000',
              amount: s.totals.grandTotal,
            )
          : null,
    );
  }

  @override
  Future<CheckoutSummary> acceptDrift(String sessionId) async {
    // The simulated drift toggle is one-shot — accept clears it.
    simulateDrift = false;
    return _require(sessionId);
  }

  @override
  Future<PriceCartResult> priceCart(PriceCartRequest req) async {
    final lines = req.lines
        .map((l) => PriceCartLine(
              productId: l.productId,
              qty: l.qty,
              unitPrice: '120.00',
              lineTotal: (120 * l.qty).toStringAsFixed(2),
            ))
        .toList(growable: false);
    final subtotal = req.lines.fold<double>(0, (s, l) => s + 120 * l.qty);
    final discount = req.couponCode != null ? subtotal * 0.05 : 0;
    final tax = (subtotal - discount) * 0.15;
    final grandTotal = subtotal - discount + tax;
    return PriceCartResult(
      lines: lines,
      totals: CheckoutTotals(
        subtotal: subtotal.toStringAsFixed(2),
        discount: discount.toStringAsFixed(2),
        tax: tax.toStringAsFixed(2),
        shipping: '0.00',
        grandTotal: grandTotal.toStringAsFixed(2),
        currency: 'SAR',
      ),
      couponValid:
          req.couponCode == null || req.couponCode!.toUpperCase() != 'INVALID',
      couponMessage: req.couponCode?.toUpperCase() == 'INVALID'
          ? 'Coupon code not recognized'
          : null,
    );
  }

  // ---- helpers ----

  CheckoutSummary _require(String sessionId) {
    final s = _sessions[sessionId];
    if (s == null) {
      throw StateError('Stub session $sessionId not found');
    }
    return s;
  }

  void _maybeDrift() {
    if (!simulateDrift) return;
    throw CheckoutDriftException(
      details: const DriftDetails(
        deltas: [
          DriftDelta(
            kind: 'price',
            productId: 'stub-1',
            before: '120.00',
            after: '125.00',
          ),
        ],
      ),
      correlationId: 'stub-drift',
    );
  }

  CheckoutTotals _computeTotals(
    List<CheckoutSummaryLine> lines, {
    required double shippingCost,
    required double discount,
  }) {
    final subtotal =
        lines.fold<double>(0, (s, l) => s + double.tryParse(l.lineTotal)!);
    final tax = (subtotal - discount) * 0.15;
    final grandTotal = subtotal - discount + tax + shippingCost;
    return CheckoutTotals(
      subtotal: subtotal.toStringAsFixed(2),
      discount: discount.toStringAsFixed(2),
      tax: tax.toStringAsFixed(2),
      shipping: shippingCost.toStringAsFixed(2),
      grandTotal: grandTotal.toStringAsFixed(2),
      currency: 'SAR',
    );
  }

  List<String> _availableMethodsFor(String marketCode) {
    if (marketCode.toLowerCase() == 'eg') {
      return const [
        'card',
        'apple_pay',
        'meeza',
        'valu',
        'bank_transfer',
        'cod',
      ];
    }
    return const [
      'card',
      'apple_pay',
      'mada',
      'stc_pay',
      'tabby',
      'tamara',
      'bank_transfer',
      'cod',
    ];
  }

  CheckoutSummary _copy(
    CheckoutSummary s, {
    CheckoutAddressDto? address,
    CheckoutShippingInfo? shipping,
    CheckoutPaymentInfo? payment,
    CheckoutTotals? totals,
    CheckoutStepStatusMap? stepStatus,
  }) {
    return CheckoutSummary(
      sessionId: s.sessionId,
      expiresAt: s.expiresAt,
      lines: s.lines,
      address: address ?? s.address,
      shipping: shipping ?? s.shipping,
      payment: payment ?? s.payment,
      totals: totals ?? s.totals,
      availableMethods: s.availableMethods,
      stepStatus: stepStatus ?? s.stepStatus,
    );
  }
}
