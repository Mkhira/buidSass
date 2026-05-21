import 'models/review_models.dart';
import 'reviews_customer_gateway.dart';

/// Deterministic in-memory [ReviewsCustomerGateway] for offline dev.
class StubReviewsCustomerGateway implements ReviewsCustomerGateway {
  StubReviewsCustomerGateway({DateTime? now}) : _now = now ?? _seedNow;

  static final DateTime _seedNow = DateTime.utc(2026, 5, 20);

  final DateTime _now;
  final Map<String, MyReviewDetail> _mine = {};

  static const _currency = 'SAR';

  /// Trim or pad an idempotency key down to an 8-char id suffix without
  /// crashing on short keys — test factories and custom callers may
  /// inject keys shorter than 8 chars.
  static String _shortId(String key) =>
      key.length >= 8 ? key.substring(0, 8) : key;

  @override
  Future<CreateReviewResult> submit({
    required CreateReviewRequest request,
    required String idempotencyKey,
  }) async {
    final id = 'rv-${_shortId(idempotencyKey)}';
    _mine[id] = MyReviewDetail(
      id: id,
      productId: request.productId,
      productName: 'Stub product',
      rating: request.rating,
      comment: request.comment,
      state: 'pending_moderation',
      createdAt: _now,
      media: const [],
      locale: request.locale,
      editableUntil: _now.add(const Duration(days: 7)),
    );
    return CreateReviewResult(
      id: id,
      state: 'pending_moderation',
      createdAt: _now,
    );
  }

  @override
  Future<MyReviewsPage> listMine(MyReviewsFilter filter) async {
    final all = [..._seedList(), ..._mine.values.map(_toListItem)];
    final filtered = filter.state == null
        ? all
        : all.where((r) => r.state == filter.state).toList(growable: false);
    final page = filter.page < 1 ? 1 : filter.page;
    final pageSize = filter.pageSize < 1 ? 20 : filter.pageSize;
    final start = (page - 1) * pageSize;
    final end = (start + pageSize) > filtered.length
        ? filtered.length
        : start + pageSize;
    final items = start >= filtered.length
        ? const <MyReviewListItem>[]
        : filtered.sublist(start, end);
    return MyReviewsPage(
      items: items,
      page: page,
      pageSize: pageSize,
      totalCount: filtered.length,
    );
  }

  @override
  Future<MyReviewDetail> getMine(String reviewId) async {
    final cached = _mine[reviewId];
    if (cached != null) return cached;
    final detail = MyReviewDetail(
      id: reviewId,
      productId: 'p-1',
      productName: 'Dental gel',
      rating: 4,
      comment: 'Solid product, fast delivery.',
      state: 'visible',
      createdAt: _now.subtract(const Duration(days: 3)),
      media: const [],
      locale: 'en',
      editableUntil: _now.add(const Duration(days: 4)),
    );
    _mine[reviewId] = detail;
    return detail;
  }

  @override
  Future<MyReviewDetail> edit({
    required String reviewId,
    required EditReviewRequest request,
  }) async {
    final existing = await getMine(reviewId);
    final next = MyReviewDetail(
      id: existing.id,
      productId: existing.productId,
      productName: existing.productName,
      rating: request.rating,
      comment: request.comment,
      state: existing.state,
      createdAt: existing.createdAt,
      media: existing.media,
      locale: existing.locale,
      editableUntil: existing.editableUntil,
      moderationNote: existing.moderationNote,
    );
    _mine[reviewId] = next;
    return next;
  }

  @override
  Future<List<ReportReason>> getReportReasons() async {
    return const [
      ReportReason(key: 'spam', label: 'Spam'),
      ReportReason(key: 'abuse', label: 'Abusive language'),
      ReportReason(key: 'fake', label: 'Fake or misleading'),
      ReportReason(key: 'other', label: 'Other'),
    ];
  }

  @override
  Future<ReportReviewResult> report({
    required String reviewId,
    required ReportReviewRequest request,
  }) async {
    return ReportReviewResult(
        id: 'rep-${reviewId.hashCode}', state: 'submitted');
  }

  List<MyReviewListItem> _seedList() {
    return [
      MyReviewListItem(
        id: 'rv-seed-1',
        productId: 'p-1',
        productName: 'Dental gel',
        rating: 5,
        state: 'visible',
        createdAt: _now.subtract(const Duration(days: 10)),
      ),
      MyReviewListItem(
        id: 'rv-seed-2',
        productId: 'p-2',
        productName: 'Mouthwash',
        rating: 3,
        state: 'pending_moderation',
        createdAt: _now.subtract(const Duration(days: 1)),
      ),
    ];
  }

  MyReviewListItem _toListItem(MyReviewDetail d) => MyReviewListItem(
        id: d.id,
        productId: d.productId,
        productName: d.productName,
        rating: d.rating,
        state: d.state,
        createdAt: d.createdAt,
      );

  // Reserved for future stub helpers that need currency formatting.
  // ignore: unused_element
  String get _currencyCode => _currency;
}
