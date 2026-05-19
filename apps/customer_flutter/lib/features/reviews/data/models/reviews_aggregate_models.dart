import 'package:flutter/foundation.dart';

/// Public, read-only review aggregate per product. Phase 2 consumes this
/// on home strips, product cards, and PDPs; Phase 7 extends the surface
/// with the customer-write side (`POST /v1/customer/reviews`).
@immutable
class ReviewsAggregate {
  const ReviewsAggregate({
    required this.productId,
    required this.ratingAverage,
    required this.ratingCount,
    required this.starHistogram,
  });

  factory ReviewsAggregate.fromJson(Map<String, Object?> json) {
    final raw = json['starHistogram'];
    final histogram = raw is List
        ? raw.whereType<num>().map((n) => n.toInt()).toList(growable: false)
        : const <int>[];
    return ReviewsAggregate(
      productId: json['productId']?.toString() ?? '',
      ratingAverage: (json['ratingAverage'] as num?)?.toDouble() ?? 0.0,
      ratingCount: (json['ratingCount'] as num?)?.toInt() ?? 0,
      starHistogram: histogram,
    );
  }

  final String productId;
  final double ratingAverage;
  final int ratingCount;

  /// `[1-star, 2-star, 3-star, 4-star, 5-star]` distribution. Empty when
  /// the batch endpoint omits the histogram per page-size guardrails.
  final List<int> starHistogram;
}
