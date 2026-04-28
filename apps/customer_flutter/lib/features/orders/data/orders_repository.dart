import '../../../core/api/i18n_aware_repository.dart';
import 'order_view_models.dart';

abstract class OrdersRepository {
  Future<OrderListPage> fetchList({
    required OrderListFilter filter,
    String? cursor,
  });

  Future<OrderDetailViewModel> fetchDetail(String orderId);
}

/// Stub adapter — emits empty list / `OrdersGapException` until spec 011's
/// generated client lands.
class StubOrdersRepository
    with I18nAwareRepository
    implements OrdersRepository {
  @override
  Future<OrderListPage> fetchList({
    required OrderListFilter filter,
    String? cursor,
  }) async {
    return const OrderListPage(items: [], nextCursor: null);
  }

  @override
  Future<OrderDetailViewModel> fetchDetail(String orderId) async {
    throw const OrdersGapException();
  }
}

class OrdersGapException implements Exception {
  const OrdersGapException();
  @override
  String toString() => 'Orders client gap — escalate to spec 011 (FR-031).';
}
