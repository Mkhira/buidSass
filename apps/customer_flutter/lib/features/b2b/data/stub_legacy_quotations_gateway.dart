import 'legacy_quotations_gateway.dart';
import 'models/legacy_quotation_models.dart';

/// Deterministic in-memory [LegacyQuotationsGateway]. Most modern
/// accounts return an empty list — toggle the seed to exercise the
/// non-empty path in dev.
class StubLegacyQuotationsGateway implements LegacyQuotationsGateway {
  StubLegacyQuotationsGateway({DateTime? now, this.seedEmpty = true})
      : _now = now ?? DateTime.utc(2026, 5, 20);

  final DateTime _now;
  final bool seedEmpty;

  @override
  Future<List<LegacyQuotationListItem>> list() async {
    if (seedEmpty) return const [];
    return [
      LegacyQuotationListItem(
        id: 'lq-1',
        quotationNumber: 'QT-2024-00045',
        state: 'pending',
        createdAt: _now.subtract(const Duration(days: 30)),
        totalAmount: '1200.00',
        totalCurrency: 'SAR',
        validUntil: _now.add(const Duration(days: 7)),
      ),
    ];
  }

  @override
  Future<LegacyQuotationDetail> getById(String id) async {
    return LegacyQuotationDetail(
      id: id,
      quotationNumber: 'QT-2024-00045',
      state: 'pending',
      createdAt: _now.subtract(const Duration(days: 30)),
      lines: const [
        LegacyQuotationLine(
          name: 'Dental gel',
          qty: 80,
          unitPrice: '15.00',
          lineTotal: '1200.00',
        ),
      ],
      subtotal: '1200.00',
      tax: '180.00',
      grandTotal: '1380.00',
      currency: 'SAR',
      terms: 'Net 30',
      validUntil: _now.add(const Duration(days: 7)),
      canAccept: true,
      canReject: true,
    );
  }

  @override
  Future<LegacyQuotationDetail> accept({
    required String id,
    required LegacyQuotationActionRequest request,
    required String idempotencyKey,
  }) async {
    final existing = await getById(id);
    return LegacyQuotationDetail(
      id: existing.id,
      quotationNumber: existing.quotationNumber,
      state: 'accepted',
      createdAt: existing.createdAt,
      lines: existing.lines,
      subtotal: existing.subtotal,
      tax: existing.tax,
      grandTotal: existing.grandTotal,
      currency: existing.currency,
      terms: existing.terms,
      validUntil: existing.validUntil,
    );
  }

  @override
  Future<LegacyQuotationDetail> reject({
    required String id,
    required LegacyQuotationActionRequest request,
    required String idempotencyKey,
  }) async {
    final existing = await getById(id);
    return LegacyQuotationDetail(
      id: existing.id,
      quotationNumber: existing.quotationNumber,
      state: 'rejected',
      createdAt: existing.createdAt,
      lines: existing.lines,
      subtotal: existing.subtotal,
      tax: existing.tax,
      grandTotal: existing.grandTotal,
      currency: existing.currency,
      terms: existing.terms,
      validUntil: existing.validUntil,
    );
  }
}
