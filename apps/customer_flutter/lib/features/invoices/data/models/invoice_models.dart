import 'package:flutter/foundation.dart';

// ============================================================
// Invoices — Phase 6 customer surface
// ============================================================
// Parses the wire shape spelled out in spec.md S-6.4 + data-model.md.
// The PDF endpoint returns a binary body and is modeled as a separate
// gateway method that hands back raw bytes.

@immutable
class InvoiceBilling {
  const InvoiceBilling({
    required this.name,
    required this.address,
    this.vatNumber,
  });
  final String name;
  final String address;
  final String? vatNumber;

  factory InvoiceBilling.fromJson(Map<String, Object?> j) => InvoiceBilling(
        name: j['name'] as String? ?? '',
        address: j['address'] as String? ?? '',
        vatNumber: j['vatNumber'] as String?,
      );
}

@immutable
class InvoiceLine {
  const InvoiceLine({
    required this.name,
    required this.qty,
    required this.unitPrice,
    required this.taxRate,
    required this.lineTotal,
  });
  final String name;
  final int qty;

  /// Decimal-string per spec.md (server formats; client never reformats
  /// for tax math — Principle 18 / BR-7).
  final String unitPrice;

  /// Decimal-string rate, e.g. "0.15" for 15% KSA VAT. UI displays as a
  /// percentage but does NOT recompute the line totals.
  final String taxRate;
  final String lineTotal;

  factory InvoiceLine.fromJson(Map<String, Object?> j) => InvoiceLine(
        name: j['name'] as String? ?? '',
        qty: (j['qty'] as num?)?.toInt() ?? 1,
        unitPrice: j['unitPrice']?.toString() ?? '0',
        taxRate: j['taxRate']?.toString() ?? '0',
        lineTotal: j['lineTotal']?.toString() ?? '0',
      );
}

@immutable
class InvoiceTotals {
  const InvoiceTotals({
    required this.subtotal,
    required this.taxTotal,
    required this.grandTotal,
  });
  final String subtotal;
  final String taxTotal;
  final String grandTotal;

  factory InvoiceTotals.fromJson(Map<String, Object?>? j) {
    if (j == null) {
      return const InvoiceTotals(
          subtotal: '0', taxTotal: '0', grandTotal: '0');
    }
    return InvoiceTotals(
      subtotal: j['subtotal']?.toString() ?? '0',
      taxTotal: j['taxTotal']?.toString() ?? '0',
      grandTotal: j['grandTotal']?.toString() ?? '0',
    );
  }
}

@immutable
class InvoicePreview {
  const InvoicePreview({
    required this.invoiceNumber,
    required this.issuedAt,
    required this.currency,
    required this.billing,
    required this.lines,
    required this.totals,
    this.downloadUrl,
  });

  final String invoiceNumber;
  final DateTime issuedAt;

  /// `SAR | EGP` — the only currencies a customer-facing invoice may
  /// carry in V1. UI never converts.
  final String currency;
  final InvoiceBilling billing;
  final List<InvoiceLine> lines;
  final InvoiceTotals totals;
  final String? downloadUrl;

  factory InvoicePreview.fromJson(Map<String, Object?> j) {
    final lines = j['lines'];
    final billing = j['billing'];
    final totals = j['totals'];
    return InvoicePreview(
      invoiceNumber: j['invoiceNumber'] as String? ?? '',
      issuedAt:
          DateTime.tryParse(j['issuedAt'] as String? ?? '') ?? DateTime.now(),
      currency: j['currency'] as String? ?? '',
      billing: billing is Map
          ? InvoiceBilling.fromJson(Map<String, Object?>.from(billing))
          : const InvoiceBilling(name: '', address: ''),
      lines: lines is List
          ? lines
              .whereType<Map>()
              .map((m) => InvoiceLine.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      totals: InvoiceTotals.fromJson(
          totals is Map ? Map<String, Object?>.from(totals) : null),
      downloadUrl: j['downloadUrl'] as String?,
    );
  }
}
