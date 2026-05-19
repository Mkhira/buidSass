import 'package:flutter/foundation.dart';

@immutable
class InventoryAvailability {
  const InventoryAvailability({
    required this.productId,
    required this.inStock,
    required this.lowStock,
    this.earliestDeliveryDate,
    this.warehouseHint,
  });

  factory InventoryAvailability.fromJson(Map<String, Object?> json) {
    final raw = json['earliestDeliveryDate']?.toString();
    return InventoryAvailability(
      productId: json['productId']?.toString() ?? '',
      inStock: json['inStock'] == true,
      lowStock: json['lowStock'] == true,
      earliestDeliveryDate:
          raw == null || raw.isEmpty ? null : DateTime.tryParse(raw),
      warehouseHint: json['warehouseHint']?.toString(),
    );
  }

  final String productId;
  final bool inStock;
  final bool lowStock;
  final DateTime? earliestDeliveryDate;
  final String? warehouseHint;

  /// Convenience: stock badge state for the StockBadge widget (Phase 2 S-2.8).
  /// Three-valued so UI doesn't have to combine booleans.
  String get badgeState {
    if (!inStock) return 'outOfStock';
    if (lowStock) return 'low';
    return 'inStock';
  }
}
