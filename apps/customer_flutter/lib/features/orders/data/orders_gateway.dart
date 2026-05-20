import 'models/order_models.dart';

/// Customer-facing orders surface — the 5 endpoints under
/// `/v1/customer/orders/` per `services/backend_api/openapi.orders.json`.
abstract class OrdersGateway {
  Future<OrderListPage> list(OrdersListFilter filter);

  Future<OrderDetail> getById(String orderId);

  Future<OrderDetail> cancel({
    required String orderId,
    required CancelOrderRequest request,
  });

  Future<ReorderResult> reorder(String orderId);

  Future<ReturnEligibility> getReturnEligibility(String orderId);
}
