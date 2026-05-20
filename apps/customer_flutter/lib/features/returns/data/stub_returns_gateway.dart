import 'dart:typed_data';

import '../../checkout/data/models/checkout_models.dart';
import 'models/return_models.dart';
import 'returns_gateway.dart';

/// Deterministic in-memory [ReturnsGateway] for offline dev.
///
/// Behavior:
/// * `list` returns a small fixed list, filterable by `status` (the wire
///   value, not the localized label).
/// * `getById` returns a fully-populated detail with timeline + photos +
///   refund.
/// * `uploadPhoto` echoes a deterministic id derived from the
///   `clientPhotoKey` so the wizard sees stable ids across retries.
/// * `create` returns a `pending` return-create result; the wizard
///   routes to detail after success.
class StubReturnsGateway implements ReturnsGateway {
  StubReturnsGateway({DateTime? now}) : _now = now ?? _seedNow;

  /// Fixed epoch so seeded timestamps stay stable across the app run.
  static final DateTime _seedNow = DateTime.utc(2026, 5, 20);
  static const _currency = 'SAR';

  final DateTime _now;

  @override
  Future<ReturnListPage> list(ReturnsListFilter filter) async {
    final all = _seedList();
    final filtered = filter.status == null
        ? all
        : all.where((r) => r.state == filter.status).toList(growable: false);
    // CodeRabbit feedback: honor `page` + `pageSize` in the stub so
    // the bloc's pagination handler exercises a realistic
    // page-sliced response rather than always seeing the same fixed
    // list. Defensive bounds keep `start >= length` returning empty.
    final page = filter.page < 1 ? 1 : filter.page;
    final pageSize = filter.pageSize < 1 ? 20 : filter.pageSize;
    final start = (page - 1) * pageSize;
    final end = (start + pageSize) > filtered.length
        ? filtered.length
        : start + pageSize;
    final items = start >= filtered.length
        ? const <ReturnListItem>[]
        : filtered.sublist(start, end);
    return ReturnListPage(
      items: items,
      page: page,
      pageSize: pageSize,
      totalCount: filtered.length,
    );
  }

  @override
  Future<ReturnDetail> getById(String id) async {
    final placed = _now.subtract(const Duration(days: 2));
    return ReturnDetail(
      id: id,
      returnNumber: 'R-2026-05-${id.hashCode.abs() % 1000000}',
      orderId: 'order-1',
      orderNumber: '2026-05-000123',
      state: 'pending',
      timeline: [
        ReturnTimelineEvent(
          kind: 'created',
          occurredAt: placed,
          actor: 'customer',
        ),
      ],
      lines: const [
        ReturnDetailLine(
          productId: 'p-1',
          name: 'Dental gel',
          qty: 1,
          reason: 'damaged',
          photos: [
            ReturnPhoto(url: 'https://stub.example/photos/1.jpg'),
          ],
        ),
      ],
    );
  }

  @override
  Future<ReturnPhotoUploadResult> uploadPhoto({
    required Uint8List bytes,
    required String filename,
    required String clientPhotoKey,
  }) async {
    // Deterministic id — stable across retries with the same key so the
    // bloc's checksum-based dedupe path is exercisable in dev.
    return ReturnPhotoUploadResult(
      photoId: 'photo-${clientPhotoKey.substring(0, 8)}',
      url: 'https://stub.example/photos/$clientPhotoKey.jpg',
      checksum: 'sha256:${bytes.length}',
    );
  }

  @override
  Future<CreateReturnResult> create({
    required String orderId,
    required CreateReturnRequest request,
    required String idempotencyKey,
  }) async {
    return CreateReturnResult(
      id: 'return-${idempotencyKey.substring(0, 8)}',
      returnNumber: 'R-2026-05-${idempotencyKey.hashCode.abs() % 1000000}',
      state: 'pending',
      createdAt: _now,
    );
  }

  List<ReturnListItem> _seedList() {
    return [
      ReturnListItem(
        id: 'return-1',
        returnNumber: 'R-2026-05-000045',
        orderId: 'order-1',
        orderNumber: '2026-05-000123',
        createdAt: _now.subtract(const Duration(days: 1)),
        state: 'pending',
      ),
      ReturnListItem(
        id: 'return-2',
        returnNumber: 'R-2026-05-000044',
        orderId: 'order-2',
        orderNumber: '2026-05-000122',
        createdAt: _now.subtract(const Duration(days: 4)),
        state: 'issued',
        refundAmount: const Money(amount: '120.00', currency: _currency),
      ),
    ];
  }
}
