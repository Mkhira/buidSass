import 'package:flutter/foundation.dart';

@immutable
class CheckoutAddress {
  const CheckoutAddress({
    required this.id,
    required this.label,
    required this.line1,
    required this.city,
    required this.country,
    required this.phone,
  });
  final String id;
  final String label;
  final String line1;
  final String city;
  final String country;
  final String phone;
}

@immutable
class ShippingQuote {
  const ShippingQuote({
    required this.id,
    required this.labelKey,
    required this.amountMinor,
    required this.currency,
    required this.etaDays,
  });
  final String id;
  final String labelKey;
  final int amountMinor;
  final String currency;
  final int etaDays;
}

@immutable
class PaymentMethod {
  const PaymentMethod({
    required this.id,
    required this.kind,
    required this.labelKey,
  });
  final String id;
  final String kind; // mada, visa, mc, applePay, stcPay, cod, bnpl
  final String labelKey;
}

@immutable
class CheckoutSession {
  const CheckoutSession({
    required this.sessionId,
    required this.availableAddresses,
    required this.availableQuotes,
    required this.availablePaymentMethods,
  });
  final String sessionId;
  final List<CheckoutAddress> availableAddresses;
  final List<ShippingQuote> availableQuotes;
  final List<PaymentMethod> availablePaymentMethods;
}

@immutable
class CheckoutOutcome {
  const CheckoutOutcome({
    required this.orderId,
    required this.orderState,
    required this.paymentState,
    required this.fulfillmentState,
    required this.refundState,
  });
  final String orderId;
  final String orderState;
  final String paymentState;
  final String fulfillmentState;
  final String refundState;
}

@immutable
class CheckoutDriftDetails {
  const CheckoutDriftDetails({
    required this.changedLines,
    required this.priceDeltaMinor,
  });
  final List<String> changedLines;
  final int priceDeltaMinor;
}
