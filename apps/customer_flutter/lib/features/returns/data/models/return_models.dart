import 'package:flutter/foundation.dart';

import '../../../checkout/data/models/checkout_models.dart';

// ============================================================
// Returns — Phase 6 customer surface
// ============================================================
// Models parse the wire shapes spelled out in
// `specs/mobile/phase-6-returns-invoices/{spec.md,data-model.md}`. All
// state values are wire strings (no client-side enum) so the server can
// add new states without forcing a client release. Renderers default
// to the raw string + a generic pill when an unknown state arrives.

/// Wire-side return states. The list is the 6-value lifecycle from
/// spec.md S-6.1 / S-6.3. Kept as a `const` set so widgets can render
/// localized labels for known values and fall through to the raw value
/// for forward-compat. Constitution Principle 24 — explicit return state
/// machine, kept separate from the order's `refundState`.
const Set<String> kKnownReturnStates = {
  'pending',
  'approved',
  'received',
  'inspected',
  'issued',
  'rejected',
};

/// Server-supplied reason codes used by S-6.2 reason picker. Mirrors the
/// fallback documented in data-model.md `POST /returns` request shape.
/// Used when the server has no enum endpoint yet (spec.md S-6.2 AC).
///
/// Codes only — display strings live in the l10n catalogs and are
/// resolved by the screen via a switch on `code`. Keeping the model
/// label-free avoids leaking English copy into UI surfaces (Principle 4).
class ReturnReason {
  const ReturnReason({required this.code});
  final String code;
}

const List<ReturnReason> kReturnReasonFallback = [
  ReturnReason(code: 'wrong_item'),
  ReturnReason(code: 'damaged'),
  ReturnReason(code: 'not_as_described'),
  ReturnReason(code: 'other'),
];

@immutable
class ReturnsListFilter {
  const ReturnsListFilter({
    this.status,
    this.page = 1,
    this.pageSize = 20,
  });

  /// Wire enum value (`pending`, `approved`, …) or `null` for "All".
  final String? status;
  final int page;
  final int pageSize;

  Map<String, Object?> toQuery() => {
        if (status != null) 'status': status,
        'page': page,
        'pageSize': pageSize,
      };

  ReturnsListFilter copyWith({
    Object? status = _sentinel,
    int? page,
    int? pageSize,
  }) {
    return ReturnsListFilter(
      status: identical(status, _sentinel) ? this.status : status as String?,
      page: page ?? this.page,
      pageSize: pageSize ?? this.pageSize,
    );
  }
}

const _sentinel = Object();

@immutable
class ReturnListItem {
  const ReturnListItem({
    required this.id,
    required this.returnNumber,
    required this.orderId,
    required this.orderNumber,
    required this.createdAt,
    required this.state,
    this.refundAmount,
  });

  final String id;
  final String returnNumber;
  final String orderId;
  final String orderNumber;
  final DateTime createdAt;

  /// Wire string from `kKnownReturnStates` (or any future server-added
  /// value). UI defaults to the raw string when unknown.
  final String state;

  /// Refund amount may be absent until the return reaches `issued`.
  final Money? refundAmount;

  factory ReturnListItem.fromJson(Map<String, Object?> j) {
    final amount = j['refundAmount'];
    return ReturnListItem(
      id: j['id'] as String? ?? '',
      returnNumber: j['returnNumber'] as String? ?? '',
      orderId: j['orderId'] as String? ?? '',
      orderNumber: j['orderNumber'] as String? ?? '',
      createdAt:
          DateTime.tryParse(j['createdAt'] as String? ?? '') ?? DateTime.now(),
      state: j['state'] as String? ?? 'pending',
      refundAmount: amount is Map
          ? Money.fromJson(Map<String, Object?>.from(amount))
          : null,
    );
  }
}

@immutable
class ReturnListPage {
  const ReturnListPage({
    required this.items,
    required this.page,
    required this.pageSize,
    required this.totalCount,
  });

  final List<ReturnListItem> items;
  final int page;
  final int pageSize;
  final int totalCount;

  bool get hasMore => page * pageSize < totalCount;

  factory ReturnListPage.fromJson(Map<String, Object?> j) {
    final items = j['items'];
    return ReturnListPage(
      items: items is List
          ? items
              .whereType<Map>()
              .map((m) => ReturnListItem.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      page: (j['page'] as num?)?.toInt() ?? 1,
      pageSize: (j['pageSize'] as num?)?.toInt() ?? 20,
      totalCount: (j['totalCount'] as num?)?.toInt() ?? 0,
    );
  }
}

@immutable
class ReturnPhoto {
  const ReturnPhoto({required this.url, this.photoId});
  final String url;
  final String? photoId;

  factory ReturnPhoto.fromJson(Map<String, Object?> j) => ReturnPhoto(
        url: j['url'] as String? ?? '',
        photoId: j['photoId'] as String?,
      );
}

@immutable
class ReturnDetailLine {
  const ReturnDetailLine({
    required this.productId,
    required this.name,
    required this.qty,
    required this.reason,
    required this.photos,
    this.note,
  });

  final String productId;
  final String name;
  final int qty;
  final String reason;
  final List<ReturnPhoto> photos;
  final String? note;

  factory ReturnDetailLine.fromJson(Map<String, Object?> j) {
    final photos = j['photos'];
    return ReturnDetailLine(
      productId: j['productId'] as String? ?? '',
      name: j['name'] as String? ?? '',
      qty: (j['qty'] as num?)?.toInt() ?? 1,
      reason: j['reason'] as String? ?? '',
      note: j['note'] as String?,
      photos: photos is List
          ? photos
              .whereType<Map>()
              .map((m) => ReturnPhoto.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
    );
  }
}

@immutable
class ReturnTimelineEvent {
  const ReturnTimelineEvent({
    required this.kind,
    required this.occurredAt,
    this.actor,
    this.note,
  });

  /// Wire kind: `created | approved | received | inspected | issued |
  /// rejected`. Unknown values render with the raw string.
  final String kind;
  final DateTime occurredAt;

  /// Wire actor: `customer | admin | system` (nullable for older rows).
  final String? actor;
  final String? note;

  factory ReturnTimelineEvent.fromJson(Map<String, Object?> j) =>
      ReturnTimelineEvent(
        kind: j['kind'] as String? ?? '',
        occurredAt: DateTime.tryParse(j['occurredAt'] as String? ?? '') ??
            DateTime.now(),
        actor: j['actor'] as String?,
        note: j['note'] as String?,
      );
}

@immutable
class ReturnRefund {
  const ReturnRefund({
    required this.amount,
    required this.currency,
    this.method,
    this.issuedAt,
  });

  final String amount;
  final String currency;
  final String? method;
  final DateTime? issuedAt;

  factory ReturnRefund.fromJson(Map<String, Object?> j) => ReturnRefund(
        amount: j['amount']?.toString() ?? '0',
        currency: j['currency'] as String? ?? '',
        method: j['method'] as String?,
        issuedAt: j['issuedAt'] is String
            ? DateTime.tryParse(j['issuedAt']! as String)
            : null,
      );
}

@immutable
class ReturnRejection {
  const ReturnRejection({this.reason, this.noteToCustomer});
  final String? reason;
  final String? noteToCustomer;

  factory ReturnRejection.fromJson(Map<String, Object?> j) => ReturnRejection(
        reason: j['reason'] as String?,
        noteToCustomer: j['noteToCustomer'] as String?,
      );
}

@immutable
class ReturnDetail {
  const ReturnDetail({
    required this.id,
    required this.returnNumber,
    required this.orderId,
    required this.orderNumber,
    required this.state,
    required this.timeline,
    required this.lines,
    this.refund,
    this.rejection,
  });

  final String id;
  final String returnNumber;
  final String orderId;
  final String orderNumber;
  final String state;
  final List<ReturnTimelineEvent> timeline;
  final List<ReturnDetailLine> lines;
  final ReturnRefund? refund;
  final ReturnRejection? rejection;

  factory ReturnDetail.fromJson(Map<String, Object?> j) {
    final timeline = j['timeline'];
    final lines = j['lines'];
    final refund = j['refund'];
    final rejection = j['rejection'];
    return ReturnDetail(
      id: j['id'] as String? ?? '',
      returnNumber: j['returnNumber'] as String? ?? '',
      orderId: j['orderId'] as String? ?? '',
      orderNumber: j['orderNumber'] as String? ?? '',
      state: j['state'] as String? ?? 'pending',
      timeline: timeline is List
          ? timeline
              .whereType<Map>()
              .map((m) =>
                  ReturnTimelineEvent.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      lines: lines is List
          ? lines
              .whereType<Map>()
              .map((m) =>
                  ReturnDetailLine.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      refund: refund is Map
          ? ReturnRefund.fromJson(Map<String, Object?>.from(refund))
          : null,
      rejection: rejection is Map
          ? ReturnRejection.fromJson(Map<String, Object?>.from(rejection))
          : null,
    );
  }
}

// ============================================================
// Photo upload + create-return request shapes
// ============================================================

@immutable
class ReturnPhotoUploadResult {
  const ReturnPhotoUploadResult({
    required this.photoId,
    required this.url,
    this.checksum,
  });

  final String photoId;
  final String url;
  final String? checksum;

  factory ReturnPhotoUploadResult.fromJson(Map<String, Object?> j) =>
      ReturnPhotoUploadResult(
        photoId: j['photoId'] as String? ?? '',
        url: j['url'] as String? ?? '',
        checksum: j['checksum'] as String?,
      );
}

/// Body of `POST /v1/customer/orders/{orderId}/returns`. See data-model.md.
@immutable
class CreateReturnLine {
  const CreateReturnLine({
    required this.productId,
    required this.qty,
    required this.reason,
    this.note,
    this.photoIds = const [],
  });

  final String productId;
  final int qty;
  final String reason;
  final String? note;
  final List<String> photoIds;

  Map<String, Object?> toJson() => {
        'productId': productId,
        'qty': qty,
        'reason': reason,
        if (note != null && note!.isNotEmpty) 'note': note,
        if (photoIds.isNotEmpty) 'photoIds': photoIds,
      };
}

@immutable
class CreateReturnRequest {
  const CreateReturnRequest({
    required this.lines,
    this.preferredRefundMethod,
  });

  final List<CreateReturnLine> lines;

  /// `original_payment | bank_transfer | wallet` (data-model.md). Server
  /// resolves the default per market when null.
  final String? preferredRefundMethod;

  Map<String, Object?> toJson() => {
        'lines': lines.map((l) => l.toJson()).toList(growable: false),
        if (preferredRefundMethod != null)
          'preferredRefundMethod': preferredRefundMethod,
      };
}

/// Response from `POST /v1/customer/orders/{orderId}/returns`. The full
/// detail is reachable via `GET /returns/{id}`; the create response is
/// only used to route forward, so we keep the parse minimal.
@immutable
class CreateReturnResult {
  const CreateReturnResult({
    required this.id,
    required this.returnNumber,
    required this.state,
    required this.createdAt,
  });

  final String id;
  final String returnNumber;
  final String state;
  final DateTime createdAt;

  factory CreateReturnResult.fromJson(Map<String, Object?> j) =>
      CreateReturnResult(
        id: j['id'] as String? ?? '',
        returnNumber: j['returnNumber'] as String? ?? '',
        state: j['state'] as String? ?? 'pending',
        createdAt: DateTime.tryParse(j['createdAt'] as String? ?? '') ??
            DateTime.now(),
      );
}
