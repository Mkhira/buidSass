import '../../checkout/data/models/checkout_models.dart';
import 'models/order_models.dart';
import 'orders_gateway.dart';

/// Deterministic in-memory [OrdersGateway] for offline dev. Returns
/// a small fixed list, a detail with all four state pills populated,
/// and synthetic reorder / return-eligibility payloads.
class StubOrdersGateway implements OrdersGateway {
  StubOrdersGateway();

  static const _currency = 'SAR';

  @override
  Future<OrderListPage> list(OrdersListFilter filter) async {
    final items = _seed
        .where((i) =>
            filter.status == null || _statusMatches(i.states, filter.status!))
        .toList(growable: false);
    return OrderListPage(
      items: items,
      page: filter.page,
      pageSize: filter.pageSize,
      totalCount: items.length,
    );
  }

  @override
  Future<OrderDetail> getById(String orderId) async {
    return _detail(orderId);
  }

  @override
  Future<OrderDetail> cancel({
    required String orderId,
    required CancelOrderRequest request,
  }) async {
    final base = _detail(orderId);
    return OrderDetail(
      id: base.id,
      orderNumber: base.orderNumber,
      placedAt: base.placedAt,
      states: const OrderStateBundle(
        orderState: 'cancelled',
        paymentState: 'refunded',
        fulfillmentState: 'pending',
        refundState: 'issued',
      ),
      actions: const OrderActions(),
      lines: base.lines,
      address: base.address,
      shipment: base.shipment,
      payment: base.payment,
      totals: base.totals,
    );
  }

  @override
  Future<ReorderResult> reorder(String orderId) async {
    final base = _detail(orderId);
    final all = base.lines;
    if (all.isEmpty) {
      return const ReorderResult(available: [], unavailable: []);
    }
    final available = all
        .take(all.length - 1)
        .map((l) => ReorderAvailableLine(
              productId: l.productId,
              qty: l.qty,
              name: l.name,
              priceHint: Money(amount: l.unitPrice, currency: _currency),
            ))
        .toList(growable: false);
    final last = all.last;
    final unavailable = [
      ReorderUnavailableLine(
        productId: last.productId,
        name: last.name,
        reason: 'out_of_stock',
      ),
    ];
    return ReorderResult(available: available, unavailable: unavailable);
  }

  @override
  Future<ReturnEligibility> getReturnEligibility(String orderId) async {
    final base = _detail(orderId);
    return ReturnEligibility(
      lines: base.lines
          .map((l) => ReturnEligibilityLine(
                productId: l.productId,
                name: l.name,
                qty: l.qty,
                eligible: true,
                windowEndsAt: base.placedAt.add(const Duration(days: 14)),
              ))
          .toList(growable: false),
      anyEligible: true,
      policyMarket: 'SA',
    );
  }

  // ---- seed ----

  bool _statusMatches(OrderStateBundle s, String status) {
    return s.orderState == status ||
        s.paymentState == status ||
        s.fulfillmentState == status;
  }

  late final List<OrderListItem> _seed = [
    OrderListItem(
      id: 'order-1',
      orderNumber: '2026-05-000123',
      placedAt: DateTime.now().subtract(const Duration(days: 1)),
      totals: const CheckoutTotals(
        subtotal: '240.00',
        discount: '0.00',
        tax: '36.00',
        shipping: '15.00',
        grandTotal: '291.00',
        currency: _currency,
      ),
      states: const OrderStateBundle(
        orderState: 'confirmed',
        paymentState: 'captured',
        fulfillmentState: 'shipped',
        refundState: 'none',
      ),
      itemPreview: const [
        OrderListItemPreview(imageUrl: '', name: 'Dental gel'),
      ],
    ),
    OrderListItem(
      id: 'order-2',
      orderNumber: '2026-05-000122',
      placedAt: DateTime.now().subtract(const Duration(days: 3)),
      totals: const CheckoutTotals(
        subtotal: '120.00',
        discount: '0.00',
        tax: '18.00',
        shipping: '15.00',
        grandTotal: '153.00',
        currency: _currency,
      ),
      states: const OrderStateBundle(
        orderState: 'placed',
        paymentState: 'pending',
        fulfillmentState: 'pending',
        refundState: 'none',
      ),
      itemPreview: const [],
    ),
  ];

  OrderDetail _detail(String orderId) {
    final placed = DateTime.now().subtract(const Duration(days: 2));
    return OrderDetail(
      id: orderId,
      orderNumber: '2026-05-${orderId.hashCode.abs() % 1000000}',
      placedAt: placed,
      states: const OrderStateBundle(
        orderState: 'confirmed',
        paymentState: 'captured',
        fulfillmentState: 'shipped',
        refundState: 'none',
      ),
      actions: const OrderActions(
        canCancel: true,
        canReorder: true,
        canRetryPayment: false,
        canReturn: true,
      ),
      lines: const [
        OrderLine(
          productId: 'p-1',
          name: 'Dental gel',
          qty: 2,
          unitPrice: '60.00',
          lineTotal: '120.00',
          imageUrl: '',
        ),
        OrderLine(
          productId: 'p-2',
          name: 'Whitening kit',
          qty: 1,
          unitPrice: '120.00',
          lineTotal: '120.00',
          imageUrl: '',
        ),
      ],
      address: const CheckoutAddressDto(
        name: 'Test Customer',
        phone: '+966500000000',
        city: 'Riyadh',
        region: 'Riyadh',
        street: '123 Olaya St',
      ),
      shipment: OrderShipment(
        method: 'Aramex Standard',
        tracking: const OrderShipmentTracking(
          carrier: 'Aramex',
          trackingNumber: 'AWB123456',
          url: 'https://aramex.example/track/AWB123456',
        ),
        events: [
          OrderTrackingEvent(
            kind: 'picked',
            occurredAt: placed.add(const Duration(hours: 4)),
            label: 'Picked from warehouse',
          ),
          OrderTrackingEvent(
            kind: 'packed',
            occurredAt: placed.add(const Duration(hours: 8)),
            label: 'Packed',
          ),
          OrderTrackingEvent(
            kind: 'shipped',
            occurredAt: placed.add(const Duration(days: 1)),
            label: 'Shipped',
          ),
        ],
      ),
      payment: const OrderPayment(method: 'card', providerRef: 'pi_stub_123'),
      totals: const CheckoutTotals(
        subtotal: '240.00',
        discount: '0.00',
        tax: '36.00',
        shipping: '15.00',
        grandTotal: '291.00',
        currency: _currency,
      ),
    );
  }
}
