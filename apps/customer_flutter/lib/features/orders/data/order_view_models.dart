import 'package:flutter/foundation.dart';

import '../../catalog/data/catalog_view_models.dart';

@immutable
class OrderListItem {
  const OrderListItem({
    required this.id,
    required this.orderNumber,
    required this.placedAt,
    required this.totalsMinor,
    required this.currency,
    required this.orderState,
    required this.paymentState,
    required this.fulfillmentState,
    required this.refundState,
  });
  final String id;
  final String orderNumber;
  final DateTime placedAt;
  final int totalsMinor;
  final String currency;
  final String orderState;
  final String paymentState;
  final String fulfillmentState;
  final String refundState;
}

@immutable
class OrderListPage {
  const OrderListPage({
    required this.items,
    required this.nextCursor,
  });
  final List<OrderListItem> items;
  final String? nextCursor;

  bool get hasMore => nextCursor != null;
}

@immutable
class OrderLineViewModel {
  const OrderLineViewModel({
    required this.productId,
    required this.sku,
    required this.name,
    required this.quantity,
    required this.unitPriceMinor,
    required this.lineSubtotalMinor,
    required this.inStock,
  });
  final String productId;
  final String sku;
  final String name;
  final int quantity;
  final int unitPriceMinor;
  final int lineSubtotalMinor;
  final bool inStock;
}

@immutable
class TrackingInfo {
  const TrackingInfo({
    required this.carrierName,
    required this.referenceNumber,
    required this.trackingUrl,
  });
  final String carrierName;
  final String referenceNumber;
  final String trackingUrl;
}

@immutable
class TimelineEvent {
  const TimelineEvent({
    required this.at,
    required this.stream,
    required this.fromState,
    required this.toState,
    this.reasonNote,
  });
  final DateTime at;
  final String stream; // order | payment | fulfillment | refund
  final String fromState;
  final String toState;
  final String? reasonNote;
}

@immutable
class RefundEligibility {
  const RefundEligibility({
    required this.canRequest,
    this.windowEndsAt,
    this.blockedReason,
  });
  final bool canRequest;
  final DateTime? windowEndsAt;
  final String? blockedReason;
}

@immutable
class OrderDetailViewModel {
  const OrderDetailViewModel({
    required this.id,
    required this.orderNumber,
    required this.placedAt,
    required this.orderState,
    required this.paymentState,
    required this.fulfillmentState,
    required this.refundState,
    required this.lines,
    required this.totals,
    required this.timeline,
    required this.refundEligibility,
    this.tracking,
    this.invoiceDownloadUrl,
  });
  final String id;
  final String orderNumber;
  final DateTime placedAt;
  final String orderState;
  final String paymentState;
  final String fulfillmentState;
  final String refundState;
  final List<OrderLineViewModel> lines;
  final PriceBreakdown totals;
  final List<TimelineEvent> timeline;
  final RefundEligibility refundEligibility;
  final TrackingInfo? tracking;
  final String? invoiceDownloadUrl;
}

@immutable
class OrderListFilter {
  const OrderListFilter({
    this.orderState,
    this.dateFrom,
    this.dateTo,
  });
  final String? orderState;
  final DateTime? dateFrom;
  final DateTime? dateTo;

  OrderListFilter copyWith({
    Object? orderState = _sentinel,
    Object? dateFrom = _sentinel,
    Object? dateTo = _sentinel,
  }) {
    return OrderListFilter(
      orderState: identical(orderState, _sentinel)
          ? this.orderState
          : orderState as String?,
      dateFrom: identical(dateFrom, _sentinel)
          ? this.dateFrom
          : dateFrom as DateTime?,
      dateTo:
          identical(dateTo, _sentinel) ? this.dateTo : dateTo as DateTime?,
    );
  }
}

const _sentinel = Object();
