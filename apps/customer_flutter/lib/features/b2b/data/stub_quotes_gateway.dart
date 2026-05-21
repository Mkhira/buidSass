import 'dart:typed_data';

import '../../checkout/data/models/checkout_models.dart' show Money;
import 'models/quote_models.dart';
import 'quotes_gateway.dart';

/// Deterministic in-memory [QuotesGateway] for offline dev.
class StubQuotesGateway implements QuotesGateway {
  StubQuotesGateway({DateTime? now}) : _now = now ?? _seedNow;

  static final DateTime _seedNow = DateTime.utc(2026, 5, 20);
  static const _currency = 'SAR';

  final DateTime _now;
  final Map<String, QuoteDetail> _details = {};

  static String _short(String key) =>
      key.length >= 8 ? key.substring(0, 8) : key;

  @override
  Future<QuotesPage> list(QuotesFilter filter) async {
    final all = _seedList();
    final filtered = filter.state == null
        ? all
        : all.where((q) => q.state == filter.state).toList(growable: false);
    final page = filter.page < 1 ? 1 : filter.page;
    final pageSize = filter.pageSize < 1 ? 20 : filter.pageSize;
    final start = (page - 1) * pageSize;
    final end = (start + pageSize) > filtered.length
        ? filtered.length
        : start + pageSize;
    return QuotesPage(
      items: start >= filtered.length
          ? const <QuoteListItem>[]
          : filtered.sublist(start, end),
      page: page,
      pageSize: pageSize,
      totalCount: filtered.length,
    );
  }

  @override
  Future<QuotesPage> awaitingMyApproval() async {
    return QuotesPage(
      items: [
        // Distinct id from `_seedList()` so list + detail don't
        // disagree on state for the same quote (CodeRabbit flag).
        // `q-pending` is the canonical awaiting-approval fixture.
        QuoteListItem(
          id: 'q-pending',
          quoteNumber: 'Q-2026-05-000044',
          state: 'awaiting_acceptance',
          createdAt: _now.subtract(const Duration(days: 1)),
          totals: const Money(amount: '850.00', currency: _currency),
          submittedAt: _now.subtract(const Duration(hours: 2)),
          submittedByName: 'Ahmed Ali',
        ),
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    );
  }

  @override
  Future<QuoteDetail> getById(String id) async {
    final cached = _details[id];
    if (cached != null) return cached;
    final detail = QuoteDetail(
      id: id,
      quoteNumber: 'Q-2026-05-000123',
      state: 'awaiting_acceptance',
      versions: [
        QuoteVersion(
          versionId: 'v1',
          publishedAt: _now.subtract(const Duration(days: 1)),
          terms: 'Net 30. Delivery within 14 days.',
          validUntil: _now.add(const Duration(days: 7)),
          lines: const [
            QuoteLine(
              productId: 'p-1',
              name: 'Dental gel — 250 ml',
              qty: 100,
              unitPrice: '15.00',
              lineTotal: '1500.00',
            ),
          ],
          totals: const QuoteTotals(
            subtotal: '1500.00',
            discount: '0.00',
            tax: '225.00',
            grandTotal: '1725.00',
            currency: _currency,
          ),
          documents: const [
            QuoteDocumentRef(
              locale: 'en',
              url: 'https://stub.example/quotes/$_currency-en.pdf',
            ),
            QuoteDocumentRef(
              locale: 'ar',
              url: 'https://stub.example/quotes/$_currency-ar.pdf',
            ),
          ],
        ),
      ],
      actions: const QuoteActions(
        canSubmitAcceptance: true,
        canFinalizeAcceptance: false,
        canRejectAcceptance: true,
        canRequestRevision: true,
        canWithdraw: true,
        canSaveAsTemplate: true,
      ),
    );
    _details[id] = detail;
    return detail;
  }

  @override
  Future<CreateQuoteResult> createFromCart({
    required CreateQuoteFromCartRequest request,
    required String idempotencyKey,
  }) =>
      _create(idempotencyKey);

  @override
  Future<CreateQuoteResult> createFromProduct({
    required CreateQuoteFromProductRequest request,
    required String idempotencyKey,
  }) =>
      _create(idempotencyKey);

  Future<CreateQuoteResult> _create(String key) async {
    return CreateQuoteResult(
      id: 'q-${_short(key)}',
      quoteNumber: 'Q-2026-05-${key.hashCode.abs() % 1000000}',
      state: 'draft',
      createdAt: _now,
    );
  }

  @override
  Future<QuoteDetail> submitAcceptance({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  }) =>
      _transition(quoteId, 'awaiting_finalization',
          allow: const QuoteActions(
            canFinalizeAcceptance: true,
            canRejectAcceptance: true,
            canWithdraw: true,
          ));

  @override
  Future<QuoteDetail> finalizeAcceptance({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  }) =>
      _transition(quoteId, 'accepted', allow: const QuoteActions());

  @override
  Future<QuoteDetail> rejectAcceptance({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  }) =>
      _transition(quoteId, 'rejected', allow: const QuoteActions());

  @override
  Future<QuoteDetail> requestRevision({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  }) =>
      _transition(quoteId, 'draft',
          allow: const QuoteActions(
            canWithdraw: true,
          ));

  @override
  Future<QuoteDetail> withdraw({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  }) =>
      _transition(quoteId, 'withdrawn', allow: const QuoteActions());

  Future<QuoteDetail> _transition(
    String quoteId,
    String state, {
    required QuoteActions allow,
  }) async {
    final existing = await getById(quoteId);
    final next = QuoteDetail(
      id: existing.id,
      quoteNumber: existing.quoteNumber,
      state: state,
      versions: existing.versions,
      actions: allow,
      submittedByName: existing.submittedByName,
      submittedAt: existing.submittedAt,
    );
    _details[quoteId] = next;
    return next;
  }

  @override
  Future<SaveAsTemplateResult> saveAsTemplate({
    required String quoteId,
    required SaveAsTemplateRequest request,
    required String idempotencyKey,
  }) async {
    return SaveAsTemplateResult(templateId: 'tpl-${_short(idempotencyKey)}');
  }

  @override
  Future<Uint8List> downloadDocument({
    required String quoteId,
    required String versionId,
    required String locale,
  }) async {
    // Tiny deterministic blob so cache-key roundtrips work without
    // pulling in a real PDF.
    final marker = '%PDF-stub-$quoteId-$versionId-$locale\n';
    return Uint8List.fromList(marker.codeUnits);
  }

  List<QuoteListItem> _seedList() {
    return [
      QuoteListItem(
        id: 'q-1',
        quoteNumber: 'Q-2026-05-000045',
        state: 'awaiting_acceptance',
        createdAt: _now.subtract(const Duration(days: 1)),
        totals: const Money(amount: '1725.00', currency: _currency),
        expiresAt: _now.add(const Duration(days: 7)),
      ),
      QuoteListItem(
        id: 'q-2',
        quoteNumber: 'Q-2026-05-000044',
        state: 'accepted',
        createdAt: _now.subtract(const Duration(days: 4)),
        totals: const Money(amount: '850.00', currency: _currency),
      ),
      QuoteListItem(
        id: 'q-3',
        quoteNumber: 'Q-2026-05-000043',
        state: 'draft',
        createdAt: _now.subtract(const Duration(days: 7)),
      ),
    ];
  }
}
