import 'package:flutter/foundation.dart';

import '../../../checkout/data/models/checkout_models.dart';

/// Four independent state machines per Phase 5 BR-1 — never merged.
/// Strings come straight from the wire so adding a new state on the
/// server doesn't require a client release. Renderers default to "?" on
/// unknown values, and the pill widget always renders 4 pills regardless.
@immutable
class OrderStateBundle {
  const OrderStateBundle({
    required this.orderState,
    required this.paymentState,
    required this.fulfillmentState,
    required this.refundState,
  });

  final String orderState;
  final String paymentState;
  final String fulfillmentState;
  final String refundState;

  factory OrderStateBundle.fromJson(Map<String, Object?>? j) {
    if (j == null) {
      return const OrderStateBundle(
        orderState: 'unknown',
        paymentState: 'unknown',
        fulfillmentState: 'unknown',
        refundState: 'none',
      );
    }
    return OrderStateBundle(
      orderState: j['orderState'] as String? ?? 'unknown',
      paymentState: j['paymentState'] as String? ?? 'unknown',
      fulfillmentState: j['fulfillmentState'] as String? ?? 'unknown',
      refundState: j['refundState'] as String? ?? 'none',
    );
  }
}

/// CTA gates from the order detail response (BR-2). Cancel / Return /
/// Retry are server-driven — UI never decides by inspecting state alone.
@immutable
class OrderActions {
  const OrderActions({
    this.canCancel = false,
    this.canReorder = false,
    this.canRetryPayment = false,
    this.canReturn = false,
  });

  final bool canCancel;
  final bool canReorder;
  final bool canRetryPayment;
  final bool canReturn;

  factory OrderActions.fromJson(Map<String, Object?>? j) {
    if (j == null) return const OrderActions();
    return OrderActions(
      canCancel: j['canCancel'] as bool? ?? false,
      canReorder: j['canReorder'] as bool? ?? false,
      canRetryPayment: j['canRetryPayment'] as bool? ?? false,
      canReturn: j['canReturn'] as bool? ?? false,
    );
  }
}

@immutable
class OrderListItem {
  const OrderListItem({
    required this.id,
    required this.orderNumber,
    required this.placedAt,
    required this.totals,
    required this.states,
    required this.itemPreview,
  });

  final String id;
  final String orderNumber;
  final DateTime placedAt;
  final CheckoutTotals totals;
  final OrderStateBundle states;
  final List<OrderListItemPreview> itemPreview;

  factory OrderListItem.fromJson(Map<String, Object?> j) {
    final preview = j['itemPreview'];
    return OrderListItem(
      id: j['id'] as String? ?? '',
      orderNumber: j['orderNumber'] as String? ?? '',
      placedAt:
          DateTime.tryParse(j['placedAt'] as String? ?? '') ?? DateTime.now(),
      totals: CheckoutTotals.fromJson(j['totals'] is Map
          ? Map<String, Object?>.from(j['totals']! as Map)
          : null),
      states: OrderStateBundle.fromJson({
        'orderState': j['orderState'],
        'paymentState': j['paymentState'],
        'fulfillmentState': j['fulfillmentState'],
        'refundState': j['refundState'],
      }),
      itemPreview: preview is List
          ? preview
              .whereType<Map>()
              .map((m) =>
                  OrderListItemPreview.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
    );
  }
}

@immutable
class OrderListItemPreview {
  const OrderListItemPreview({required this.imageUrl, required this.name});
  final String imageUrl;
  final String name;

  factory OrderListItemPreview.fromJson(Map<String, Object?> j) =>
      OrderListItemPreview(
        imageUrl: j['imageUrl'] as String? ?? '',
        name: j['name'] as String? ?? '',
      );
}

@immutable
class OrderListPage {
  const OrderListPage({
    required this.items,
    required this.page,
    required this.pageSize,
    required this.totalCount,
  });

  final List<OrderListItem> items;
  final int page;
  final int pageSize;
  final int totalCount;

  bool get hasMore => page * pageSize < totalCount;

  factory OrderListPage.fromJson(Map<String, Object?> j) {
    final items = j['items'];
    return OrderListPage(
      items: items is List
          ? items
              .whereType<Map>()
              .map((m) => OrderListItem.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      page: (j['page'] as num?)?.toInt() ?? 1,
      pageSize: (j['pageSize'] as num?)?.toInt() ?? 20,
      totalCount: (j['totalCount'] as num?)?.toInt() ?? 0,
    );
  }
}

@immutable
class OrderLine {
  const OrderLine({
    required this.productId,
    required this.name,
    required this.qty,
    required this.unitPrice,
    required this.lineTotal,
    required this.imageUrl,
  });

  final String productId;
  final String name;
  final int qty;
  final String unitPrice;
  final String lineTotal;
  final String imageUrl;

  factory OrderLine.fromJson(Map<String, Object?> j) => OrderLine(
        productId: j['productId'] as String? ?? '',
        name: j['name'] as String? ?? '',
        qty: (j['qty'] as num?)?.toInt() ?? 1,
        unitPrice: j['unitPrice']?.toString() ?? '0',
        lineTotal: j['lineTotal']?.toString() ?? '0',
        imageUrl: j['imageUrl'] as String? ?? '',
      );
}

@immutable
class OrderShipmentTracking {
  const OrderShipmentTracking({
    required this.carrier,
    required this.trackingNumber,
    this.url,
  });
  final String carrier;
  final String trackingNumber;
  final String? url;

  factory OrderShipmentTracking.fromJson(Map<String, Object?> j) =>
      OrderShipmentTracking(
        carrier: j['carrier'] as String? ?? '',
        trackingNumber: j['trackingNumber'] as String? ?? '',
        url: j['url'] as String?,
      );
}

@immutable
class OrderTrackingEvent {
  const OrderTrackingEvent({
    required this.kind,
    required this.occurredAt,
    required this.label,
  });

  final String kind;
  final DateTime occurredAt;
  final String label;

  factory OrderTrackingEvent.fromJson(Map<String, Object?> j) =>
      OrderTrackingEvent(
        kind: j['kind'] as String? ?? '',
        occurredAt: DateTime.tryParse(j['occurredAt'] as String? ?? '') ??
            DateTime.now(),
        label: j['label'] as String? ?? '',
      );
}

@immutable
class OrderShipment {
  const OrderShipment({
    this.method,
    this.tracking,
    this.events = const [],
  });
  final String? method;
  final OrderShipmentTracking? tracking;
  final List<OrderTrackingEvent> events;

  factory OrderShipment.fromJson(Map<String, Object?>? j) {
    if (j == null) return const OrderShipment();
    final tracking = j['tracking'];
    final events = j['events'];
    return OrderShipment(
      method: j['method'] as String?,
      tracking: tracking is Map
          ? OrderShipmentTracking.fromJson(Map<String, Object?>.from(tracking))
          : null,
      events: events is List
          ? events
              .whereType<Map>()
              .map((m) =>
                  OrderTrackingEvent.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
    );
  }
}

@immutable
class OrderPayment {
  const OrderPayment({
    this.method,
    this.providerRef,
    this.bankTransfer,
  });
  final String? method;
  final String? providerRef;
  final BankTransferDetails? bankTransfer;

  factory OrderPayment.fromJson(Map<String, Object?>? j) {
    if (j == null) return const OrderPayment();
    final bt = j['bankTransfer'];
    return OrderPayment(
      method: j['method'] as String?,
      providerRef: j['providerRef'] as String?,
      bankTransfer: bt is Map
          ? BankTransferDetails.fromJson(Map<String, Object?>.from(bt))
          : null,
    );
  }
}

@immutable
class OrderDetail {
  const OrderDetail({
    required this.id,
    required this.orderNumber,
    required this.placedAt,
    required this.states,
    required this.actions,
    required this.lines,
    this.address,
    required this.shipment,
    required this.payment,
    required this.totals,
  });

  final String id;
  final String orderNumber;
  final DateTime placedAt;
  final OrderStateBundle states;
  final OrderActions actions;
  final List<OrderLine> lines;
  final CheckoutAddressDto? address;
  final OrderShipment shipment;
  final OrderPayment payment;
  final CheckoutTotals totals;

  factory OrderDetail.fromJson(Map<String, Object?> j) {
    final lines = j['lines'];
    final addr = j['address'];
    return OrderDetail(
      id: j['id'] as String? ?? '',
      orderNumber: j['orderNumber'] as String? ?? '',
      placedAt:
          DateTime.tryParse(j['placedAt'] as String? ?? '') ?? DateTime.now(),
      states: OrderStateBundle.fromJson(j['states'] is Map
          ? Map<String, Object?>.from(j['states']! as Map)
          : null),
      actions: OrderActions.fromJson(j['actions'] is Map
          ? Map<String, Object?>.from(j['actions']! as Map)
          : null),
      lines: lines is List
          ? lines
              .whereType<Map>()
              .map((m) => OrderLine.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      address: addr is Map
          ? CheckoutAddressDto.fromJson(Map<String, Object?>.from(addr))
          : null,
      shipment: OrderShipment.fromJson(j['shipment'] is Map
          ? Map<String, Object?>.from(j['shipment']! as Map)
          : null),
      payment: OrderPayment.fromJson(j['payment'] is Map
          ? Map<String, Object?>.from(j['payment']! as Map)
          : null),
      totals: CheckoutTotals.fromJson(j['totals'] is Map
          ? Map<String, Object?>.from(j['totals']! as Map)
          : null),
    );
  }
}

// ===== Cancel =====

@immutable
class CancelReason {
  const CancelReason({required this.code, required this.label});
  final String code;
  final String label;
}

/// Hardcoded fallback list — used when the server has no enum endpoint
/// yet. Documented per spec.md S-5.3 AC.
const List<CancelReason> kCancelReasonFallback = [
  CancelReason(code: 'changed_mind', label: 'Changed my mind'),
  CancelReason(code: 'ordered_wrong_item', label: 'Ordered the wrong item'),
  CancelReason(code: 'delivery_too_slow', label: 'Delivery too slow'),
  CancelReason(code: 'found_better_price', label: 'Found a better price'),
  CancelReason(code: 'other', label: 'Other'),
];

@immutable
class CancelOrderRequest {
  const CancelOrderRequest({required this.reason, this.note});
  final String reason;
  final String? note;

  Map<String, Object?> toJson() => {
        'reason': reason,
        if (note != null && note!.isNotEmpty) 'note': note,
      };
}

// ===== Reorder =====

@immutable
class ReorderAvailableLine {
  const ReorderAvailableLine({
    required this.productId,
    required this.qty,
    required this.name,
    required this.priceHint,
  });
  final String productId;
  final int qty;
  final String name;
  final Money priceHint;

  factory ReorderAvailableLine.fromJson(Map<String, Object?> j) =>
      ReorderAvailableLine(
        productId: j['productId'] as String? ?? '',
        qty: (j['qty'] as num?)?.toInt() ?? 1,
        name: j['name'] as String? ?? '',
        priceHint: Money.fromJson(j['priceHint']),
      );
}

@immutable
class ReorderUnavailableLine {
  const ReorderUnavailableLine({
    required this.productId,
    required this.name,
    required this.reason,
  });
  final String productId;
  final String name;
  final String reason; // out_of_stock | discontinued | market_blocked

  factory ReorderUnavailableLine.fromJson(Map<String, Object?> j) =>
      ReorderUnavailableLine(
        productId: j['productId'] as String? ?? '',
        name: j['name'] as String? ?? '',
        reason: j['reason'] as String? ?? '',
      );
}

@immutable
class ReorderResult {
  const ReorderResult({required this.available, required this.unavailable});
  final List<ReorderAvailableLine> available;
  final List<ReorderUnavailableLine> unavailable;

  factory ReorderResult.fromJson(Map<String, Object?> j) {
    final a = j['available'];
    final u = j['unavailable'];
    return ReorderResult(
      available: a is List
          ? a
              .whereType<Map>()
              .map((m) =>
                  ReorderAvailableLine.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      unavailable: u is List
          ? u
              .whereType<Map>()
              .map((m) =>
                  ReorderUnavailableLine.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
    );
  }
}

// ===== Return eligibility =====

@immutable
class ReturnEligibilityLine {
  const ReturnEligibilityLine({
    required this.productId,
    required this.name,
    required this.qty,
    required this.eligible,
    this.reason,
    this.windowEndsAt,
  });

  final String productId;
  final String name;
  final int qty;
  final bool eligible;
  final String? reason;
  final DateTime? windowEndsAt;

  factory ReturnEligibilityLine.fromJson(Map<String, Object?> j) =>
      ReturnEligibilityLine(
        productId: j['productId'] as String? ?? '',
        name: j['name'] as String? ?? '',
        qty: (j['qty'] as num?)?.toInt() ?? 1,
        eligible: j['eligible'] as bool? ?? false,
        reason: j['reason'] as String?,
        windowEndsAt: j['windowEndsAt'] is String
            ? DateTime.tryParse(j['windowEndsAt']! as String)
            : null,
      );
}

@immutable
class ReturnEligibility {
  const ReturnEligibility({
    required this.lines,
    required this.anyEligible,
    this.policyMarket,
  });
  final List<ReturnEligibilityLine> lines;
  final bool anyEligible;
  final String? policyMarket;

  factory ReturnEligibility.fromJson(Map<String, Object?> j) {
    final lines = j['lines'];
    return ReturnEligibility(
      lines: lines is List
          ? lines
              .whereType<Map>()
              .map((m) =>
                  ReturnEligibilityLine.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      anyEligible: j['anyEligible'] as bool? ?? false,
      policyMarket: j['policyMarket'] as String?,
    );
  }
}

// ===== List filters =====

@immutable
class OrdersListFilter {
  const OrdersListFilter({
    this.status,
    this.from,
    this.to,
    this.page = 1,
    this.pageSize = 20,
  });

  final String? status;
  final DateTime? from;
  final DateTime? to;
  final int page;
  final int pageSize;

  Map<String, Object?> toQuery() => {
        if (status != null) 'status': status,
        if (from != null) 'from': from!.toIso8601String(),
        if (to != null) 'to': to!.toIso8601String(),
        'page': page,
        'pageSize': pageSize,
      };

  OrdersListFilter copyWith({
    Object? status = _sentinel,
    Object? from = _sentinel,
    Object? to = _sentinel,
    int? page,
    int? pageSize,
  }) {
    return OrdersListFilter(
      status: identical(status, _sentinel) ? this.status : status as String?,
      from: identical(from, _sentinel) ? this.from : from as DateTime?,
      to: identical(to, _sentinel) ? this.to : to as DateTime?,
      page: page ?? this.page,
      pageSize: pageSize ?? this.pageSize,
    );
  }
}

const _sentinel = Object();
