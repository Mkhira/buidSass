import 'package:flutter/foundation.dart';

// ============================================================
// Legacy quotations — Phase 8 read-only flow
// ============================================================
// Surfaces the predecessor-model `quotations` endpoints. Most accounts
// won't have any. The list/detail are kept deliberately small — server
// renders the canonical line totals and we don't echo them anywhere
// else.

@immutable
class LegacyQuotationListItem {
  const LegacyQuotationListItem({
    required this.id,
    required this.quotationNumber,
    required this.state,
    required this.createdAt,
    this.totalAmount,
    this.totalCurrency,
    this.validUntil,
  });

  final String id;
  final String quotationNumber;
  final String state;
  final DateTime createdAt;
  final String? totalAmount;
  final String? totalCurrency;
  final DateTime? validUntil;

  factory LegacyQuotationListItem.fromJson(Map<String, Object?> j) {
    final total = j['total'];
    return LegacyQuotationListItem(
      id: j['id'] as String? ?? '',
      quotationNumber: j['quotationNumber'] as String? ?? '',
      state: j['state'] as String? ?? 'pending',
      createdAt:
          DateTime.tryParse(j['createdAt'] as String? ?? '') ?? DateTime.now(),
      totalAmount: total is Map ? total['amount']?.toString() : null,
      totalCurrency: total is Map ? total['currency'] as String? : null,
      validUntil: j['validUntil'] is String
          ? DateTime.tryParse(j['validUntil']! as String)
          : null,
    );
  }
}

@immutable
class LegacyQuotationLine {
  const LegacyQuotationLine({
    required this.name,
    required this.qty,
    required this.unitPrice,
    required this.lineTotal,
  });

  final String name;
  final int qty;
  final String unitPrice;
  final String lineTotal;

  factory LegacyQuotationLine.fromJson(Map<String, Object?> j) =>
      LegacyQuotationLine(
        name: j['name'] as String? ?? '',
        qty: (j['qty'] as num?)?.toInt() ?? 1,
        unitPrice: j['unitPrice']?.toString() ?? '0',
        lineTotal: j['lineTotal']?.toString() ?? '0',
      );
}

@immutable
class LegacyQuotationDetail {
  const LegacyQuotationDetail({
    required this.id,
    required this.quotationNumber,
    required this.state,
    required this.createdAt,
    required this.lines,
    required this.subtotal,
    required this.tax,
    required this.grandTotal,
    required this.currency,
    this.terms,
    this.validUntil,
    this.canAccept = false,
    this.canReject = false,
  });

  final String id;
  final String quotationNumber;
  final String state;
  final DateTime createdAt;
  final List<LegacyQuotationLine> lines;
  final String subtotal;
  final String tax;
  final String grandTotal;
  final String currency;
  final String? terms;
  final DateTime? validUntil;
  final bool canAccept;
  final bool canReject;

  factory LegacyQuotationDetail.fromJson(Map<String, Object?> j) {
    final lines = j['lines'];
    final totals = j['totals'];
    final actions = j['actions'];
    return LegacyQuotationDetail(
      id: j['id'] as String? ?? '',
      quotationNumber: j['quotationNumber'] as String? ?? '',
      state: j['state'] as String? ?? 'pending',
      createdAt:
          DateTime.tryParse(j['createdAt'] as String? ?? '') ?? DateTime.now(),
      lines: lines is List
          ? lines
              .whereType<Map>()
              .map((m) =>
                  LegacyQuotationLine.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      subtotal: totals is Map ? totals['subtotal']?.toString() ?? '0' : '0',
      tax: totals is Map ? totals['tax']?.toString() ?? '0' : '0',
      grandTotal: totals is Map ? totals['grandTotal']?.toString() ?? '0' : '0',
      currency: totals is Map ? (totals['currency'] as String? ?? '') : '',
      terms: j['terms'] as String?,
      validUntil: j['validUntil'] is String
          ? DateTime.tryParse(j['validUntil']! as String)
          : null,
      canAccept: actions is Map ? actions['canAccept'] == true : false,
      canReject: actions is Map ? actions['canReject'] == true : false,
    );
  }
}

@immutable
class LegacyQuotationActionRequest {
  const LegacyQuotationActionRequest({this.note});
  final String? note;

  Map<String, Object?> toJson() => {
        if (note != null && note!.isNotEmpty) 'note': note,
      };
}
