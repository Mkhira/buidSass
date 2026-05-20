import 'package:flutter/foundation.dart';

/// All checkout-flow data models. Mirrors the wire shapes in
/// `services/backend_api/openapi.checkout.json` + Phase 4 data-model.md.

// ===== Money =====

@immutable
class Money {
  const Money({required this.amount, required this.currency});

  /// Decimal-as-string per server convention (e.g. "120.00"). Client
  /// formats via `intl` `NumberFormat.currency` at render time — never
  /// parses to double for arithmetic (BR-2: totals come from server).
  final String amount;
  final String currency;

  Map<String, Object?> toJson() => {'amount': amount, 'currency': currency};

  factory Money.fromJson(Object? raw) {
    if (raw is Map) {
      return Money(
        amount: raw['amount']?.toString() ?? '0',
        currency: raw['currency']?.toString() ?? '',
      );
    }
    return const Money(amount: '0', currency: '');
  }
}

// ===== Address =====

@immutable
class CheckoutAddressDto {
  const CheckoutAddressDto({
    this.addressId,
    required this.name,
    required this.phone,
    required this.city,
    required this.region,
    required this.street,
    this.postalCode,
  });

  final String? addressId;
  final String name;
  final String phone;
  final String city;
  final String region;
  final String street;
  final String? postalCode;

  Map<String, Object?> toJson() => {
        if (addressId != null) 'addressId': addressId,
        'name': name,
        'phone': phone,
        'city': city,
        'region': region,
        'street': street,
        if (postalCode != null) 'postalCode': postalCode,
      };

  factory CheckoutAddressDto.fromJson(Map<String, Object?> j) =>
      CheckoutAddressDto(
        addressId: j['addressId'] as String?,
        name: j['name'] as String? ?? '',
        phone: j['phone'] as String? ?? '',
        city: j['city'] as String? ?? '',
        region: j['region'] as String? ?? '',
        street: j['street'] as String? ?? '',
        postalCode: j['postalCode'] as String?,
      );
}

// ===== Summary =====

@immutable
class CheckoutSummaryLine {
  const CheckoutSummaryLine({
    required this.productId,
    required this.name,
    required this.qty,
    required this.unitPrice,
    required this.lineTotal,
  });

  final String productId;
  final String name;
  final int qty;
  final String unitPrice;
  final String lineTotal;

  factory CheckoutSummaryLine.fromJson(Map<String, Object?> j) =>
      CheckoutSummaryLine(
        productId: j['productId'] as String? ?? '',
        name: j['name'] as String? ?? '',
        qty: (j['qty'] as num?)?.toInt() ?? 1,
        unitPrice: j['unitPrice']?.toString() ?? '0',
        lineTotal: j['lineTotal']?.toString() ?? '0',
      );
}

@immutable
class CheckoutShippingInfo {
  const CheckoutShippingInfo({this.method, this.cost, this.etaDays});
  final String? method;
  final Money? cost;
  final String? etaDays;

  factory CheckoutShippingInfo.fromJson(Map<String, Object?>? j) {
    if (j == null) return const CheckoutShippingInfo();
    return CheckoutShippingInfo(
      method: j['method'] as String?,
      cost: j['cost'] == null ? null : Money.fromJson(j['cost']),
      etaDays: j['etaDays'] as String?,
    );
  }
}

@immutable
class CheckoutPaymentInfo {
  const CheckoutPaymentInfo({this.method});
  final String? method;
  factory CheckoutPaymentInfo.fromJson(Map<String, Object?>? j) =>
      CheckoutPaymentInfo(method: j?['method'] as String?);
}

@immutable
class CheckoutTotals {
  const CheckoutTotals({
    required this.subtotal,
    required this.discount,
    required this.tax,
    required this.shipping,
    required this.grandTotal,
    required this.currency,
  });

  final String subtotal;
  final String discount;
  final String tax;
  final String shipping;
  final String grandTotal;
  final String currency;

  factory CheckoutTotals.fromJson(Map<String, Object?>? j) {
    if (j == null) {
      return const CheckoutTotals(
        subtotal: '0',
        discount: '0',
        tax: '0',
        shipping: '0',
        grandTotal: '0',
        currency: '',
      );
    }
    return CheckoutTotals(
      subtotal: j['subtotal']?.toString() ?? '0',
      discount: j['discount']?.toString() ?? '0',
      tax: j['tax']?.toString() ?? '0',
      shipping: j['shipping']?.toString() ?? '0',
      grandTotal: j['grandTotal']?.toString() ?? '0',
      currency: j['currency']?.toString() ?? '',
    );
  }
}

/// Per-step completion status — drives the stepper indicator.
enum CheckoutStepStatus { pending, complete }

CheckoutStepStatus _parseStepStatus(Object? v) {
  return v == 'complete'
      ? CheckoutStepStatus.complete
      : CheckoutStepStatus.pending;
}

@immutable
class CheckoutStepStatusMap {
  const CheckoutStepStatusMap({
    this.address = CheckoutStepStatus.pending,
    this.shipping = CheckoutStepStatus.pending,
    this.payment = CheckoutStepStatus.pending,
    this.review = CheckoutStepStatus.pending,
  });

  final CheckoutStepStatus address;
  final CheckoutStepStatus shipping;
  final CheckoutStepStatus payment;
  final CheckoutStepStatus review;

  factory CheckoutStepStatusMap.fromJson(Map<String, Object?>? j) {
    if (j == null) return const CheckoutStepStatusMap();
    return CheckoutStepStatusMap(
      address: _parseStepStatus(j['address']),
      shipping: _parseStepStatus(j['shipping']),
      payment: _parseStepStatus(j['payment']),
      review: _parseStepStatus(j['review']),
    );
  }
}

@immutable
class CheckoutSummary {
  const CheckoutSummary({
    required this.sessionId,
    required this.expiresAt,
    required this.lines,
    this.address,
    required this.shipping,
    required this.payment,
    required this.totals,
    required this.availableMethods,
    required this.stepStatus,
  });

  final String sessionId;
  final DateTime expiresAt;
  final List<CheckoutSummaryLine> lines;
  final CheckoutAddressDto? address;
  final CheckoutShippingInfo shipping;
  final CheckoutPaymentInfo payment;
  final CheckoutTotals totals;
  final List<String> availableMethods;
  final CheckoutStepStatusMap stepStatus;

  factory CheckoutSummary.fromJson(Map<String, Object?> j) {
    final lines = j['lines'];
    final methods = j['availableMethods'];
    final addr = j['address'];
    return CheckoutSummary(
      sessionId: j['sessionId'] as String? ?? '',
      expiresAt: DateTime.tryParse(j['expiresAt'] as String? ?? '') ??
          DateTime.now().add(const Duration(minutes: 30)),
      lines: lines is List
          ? lines
              .whereType<Map>()
              .map((m) =>
                  CheckoutSummaryLine.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      address: addr is Map
          ? CheckoutAddressDto.fromJson(Map<String, Object?>.from(addr))
          : null,
      shipping: CheckoutShippingInfo.fromJson(j['shipping'] is Map
          ? Map<String, Object?>.from(j['shipping']! as Map)
          : null),
      payment: CheckoutPaymentInfo.fromJson(j['payment'] is Map
          ? Map<String, Object?>.from(j['payment']! as Map)
          : null),
      totals: CheckoutTotals.fromJson(j['totals'] is Map
          ? Map<String, Object?>.from(j['totals']! as Map)
          : null),
      availableMethods:
          methods is List ? methods.whereType<String>().toList() : const [],
      stepStatus: CheckoutStepStatusMap.fromJson(j['stepStatus'] is Map
          ? Map<String, Object?>.from(j['stepStatus']! as Map)
          : null),
    );
  }
}

// ===== Session creation =====

@immutable
class CreateSessionRequest {
  const CreateSessionRequest({
    required this.lines,
    this.couponCode,
    required this.buyerKind,
    required this.marketCode,
  });

  final List<CreateSessionLine> lines;
  final String? couponCode;
  final String buyerKind; // consumer | business
  final String marketCode;

  Map<String, Object?> toJson() => {
        'lines': lines.map((l) => l.toJson()).toList(growable: false),
        if (couponCode != null) 'couponCode': couponCode,
        'buyerKind': buyerKind,
        'marketCode': marketCode,
      };
}

@immutable
class CreateSessionLine {
  const CreateSessionLine({required this.productId, required this.qty});
  final String productId;
  final int qty;
  Map<String, Object?> toJson() => {'productId': productId, 'qty': qty};
}

@immutable
class CreateSessionResult {
  const CreateSessionResult({
    required this.sessionId,
    required this.expiresAt,
    required this.summary,
    required this.availableSteps,
  });

  final String sessionId;
  final DateTime expiresAt;
  final CheckoutSummary summary;
  final List<String> availableSteps;

  factory CreateSessionResult.fromJson(Map<String, Object?> j) {
    final steps = j['availableSteps'];
    final summary = j['summary'];
    return CreateSessionResult(
      sessionId: j['sessionId'] as String? ?? '',
      expiresAt: DateTime.tryParse(j['expiresAt'] as String? ?? '') ??
          DateTime.now().add(const Duration(minutes: 30)),
      summary: summary is Map
          ? CheckoutSummary.fromJson(Map<String, Object?>.from(summary))
          // Server may omit summary on create — synthesize a minimal one
          // so the step screens have something to render before the
          // mandatory subsequent GET /summary call.
          : CheckoutSummary.fromJson({
              'sessionId': j['sessionId'],
              'expiresAt': j['expiresAt'],
            }),
      availableSteps: steps is List
          ? steps.whereType<String>().toList(growable: false)
          : const ['address', 'shipping', 'payment', 'review'],
    );
  }
}

// ===== Shipping quotes =====

@immutable
class ShippingQuoteOption {
  const ShippingQuoteOption({
    required this.method,
    required this.label,
    required this.price,
    required this.etaDays,
  });

  final String method;
  final String label;
  final Money price;
  final String etaDays;

  factory ShippingQuoteOption.fromJson(Map<String, Object?> j) =>
      ShippingQuoteOption(
        method: j['method'] as String? ?? '',
        label: j['label'] as String? ?? '',
        price: Money.fromJson(j['price']),
        etaDays: j['etaDays'] as String? ?? '',
      );
}

// ===== Submit =====

@immutable
class BankTransferDetails {
  const BankTransferDetails({
    required this.reference,
    required this.iban,
    required this.amount,
  });
  final String reference;
  final String iban;
  final String amount;
  factory BankTransferDetails.fromJson(Map<String, Object?> j) =>
      BankTransferDetails(
        reference: j['reference'] as String? ?? '',
        iban: j['iban'] as String? ?? '',
        amount: j['amount']?.toString() ?? '',
      );
}

@immutable
class SubmitRedirect {
  const SubmitRedirect({required this.kind, this.url});
  final String kind; // 3ds | provider_webview | none
  final String? url;
  factory SubmitRedirect.fromJson(Map<String, Object?>? j) {
    if (j == null) return const SubmitRedirect(kind: 'none');
    return SubmitRedirect(
      kind: j['kind'] as String? ?? 'none',
      url: j['url'] as String?,
    );
  }
}

@immutable
class SubmitResult {
  const SubmitResult({
    required this.orderId,
    required this.orderNumber,
    required this.paymentState,
    required this.fulfillmentState,
    required this.redirect,
    this.bankTransfer,
  });

  final String orderId;
  final String orderNumber;
  final String paymentState; // captured | pending | requires_action
  final String fulfillmentState;
  final SubmitRedirect redirect;
  final BankTransferDetails? bankTransfer;

  factory SubmitResult.fromJson(Map<String, Object?> j) {
    final bt = j['bankTransfer'];
    return SubmitResult(
      orderId: j['orderId'] as String? ?? '',
      orderNumber: j['orderNumber'] as String? ?? '',
      paymentState: j['paymentState'] as String? ?? 'pending',
      fulfillmentState: j['fulfillmentState'] as String? ?? 'pending',
      redirect: SubmitRedirect.fromJson(j['redirect'] is Map
          ? Map<String, Object?>.from(j['redirect']! as Map)
          : null),
      bankTransfer: bt is Map
          ? BankTransferDetails.fromJson(Map<String, Object?>.from(bt))
          : null,
    );
  }
}

// ===== Drift =====

@immutable
class DriftDelta {
  const DriftDelta({
    required this.kind,
    required this.productId,
    this.before,
    this.after,
  });
  final String kind; // price | qty | unavailable
  final String productId;
  final String? before;
  final String? after;

  factory DriftDelta.fromJson(Map<String, Object?> j) => DriftDelta(
        kind: j['kind'] as String? ?? '',
        productId: j['productId'] as String? ?? '',
        before: j['before']?.toString(),
        after: j['after']?.toString(),
      );
}

@immutable
class DriftDetails {
  const DriftDetails({required this.deltas, this.newTotals});
  final List<DriftDelta> deltas;
  final CheckoutTotals? newTotals;

  factory DriftDetails.fromJson(Map<String, Object?> j) {
    final deltas = j['deltas'];
    final totals = j['newTotals'];
    return DriftDetails(
      deltas: deltas is List
          ? deltas
              .whereType<Map>()
              .map((m) => DriftDelta.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      newTotals: totals is Map
          ? CheckoutTotals.fromJson(Map<String, Object?>.from(totals))
          : null,
    );
  }
}

/// Thrown by the gateway on any 409 response — carries the parsed
/// `details` payload so the bloc layer can render `ConflictDialog`
/// without re-parsing.
class CheckoutDriftException implements Exception {
  CheckoutDriftException({required this.details, this.correlationId});
  final DriftDetails details;
  final String? correlationId;

  @override
  String toString() =>
      'CheckoutDriftException(deltas=${details.deltas.length})';
}

// ===== Price-cart preview =====

@immutable
class PriceCartRequest {
  const PriceCartRequest({
    required this.lines,
    this.couponCode,
    required this.buyerKind,
    required this.marketCode,
  });
  final List<CreateSessionLine> lines;
  final String? couponCode;
  final String buyerKind;
  final String marketCode;

  Map<String, Object?> toJson() => {
        'lines': lines.map((l) => l.toJson()).toList(growable: false),
        if (couponCode != null) 'couponCode': couponCode,
        'buyerKind': buyerKind,
        'marketCode': marketCode,
      };
}

@immutable
class PriceCartLine {
  const PriceCartLine({
    required this.productId,
    required this.qty,
    required this.unitPrice,
    required this.lineTotal,
  });
  final String productId;
  final int qty;
  final String unitPrice;
  final String lineTotal;

  factory PriceCartLine.fromJson(Map<String, Object?> j) => PriceCartLine(
        productId: j['productId'] as String? ?? '',
        qty: (j['qty'] as num?)?.toInt() ?? 1,
        unitPrice: j['unitPrice']?.toString() ?? '0',
        lineTotal: j['lineTotal']?.toString() ?? '0',
      );
}

@immutable
class PriceCartResult {
  const PriceCartResult({
    required this.lines,
    required this.totals,
    this.couponValid = true,
    this.couponMessage,
  });

  final List<PriceCartLine> lines;
  final CheckoutTotals totals;
  final bool couponValid;
  final String? couponMessage;

  factory PriceCartResult.fromJson(Map<String, Object?> j) {
    final lines = j['lines'];
    return PriceCartResult(
      lines: lines is List
          ? lines
              .whereType<Map>()
              .map((m) => PriceCartLine.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      totals: CheckoutTotals.fromJson(j['totals'] is Map
          ? Map<String, Object?>.from(j['totals']! as Map)
          : null),
      couponValid: j['couponValid'] as bool? ?? true,
      couponMessage: j['couponMessage'] as String?,
    );
  }
}
