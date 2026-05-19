import 'package:dio/dio.dart';

import '../../../core/error/error_mapper.dart';
import 'models/reviews_aggregate_models.dart';
import 'reviews_aggregates_gateway.dart';

class ReviewsAggregatesGatewayImpl implements ReviewsAggregatesGateway {
  ReviewsAggregatesGatewayImpl({required Dio dio, ErrorMapper? errorMapper})
      : _dio = dio,
        _errors = errorMapper ?? const ErrorMapper();

  static const _batchPath = '/v1/public/reviews/aggregates';

  final Dio _dio;
  final ErrorMapper _errors;

  @override
  Future<List<ReviewsAggregate>> getAggregatesBatch({
    required List<String> productIds,
    required String marketCode,
  }) async {
    if (productIds.isEmpty) return const [];
    try {
      final res = await _dio.get<Object?>(
        _batchPath,
        queryParameters: {
          'product_ids': productIds.join(','),
          'market_code': marketCode,
        },
      );
      final data = res.data;
      if (data is! List) return const [];
      return data
          .whereType<Map>()
          .map((m) =>
              ReviewsAggregate.fromJson(Map<String, Object?>.from(m)))
          .toList(growable: false);
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<ReviewsAggregate?> getAggregate({
    required String productId,
    required String marketCode,
  }) async {
    try {
      final res = await _dio.get<Object?>(
        '$_batchPath/$productId',
        queryParameters: {'market_code': marketCode},
      );
      final data = res.data;
      if (data is Map) {
        return ReviewsAggregate.fromJson(Map<String, Object?>.from(data));
      }
      // Some servers return an array of length 1 instead of an object.
      if (data is List && data.isNotEmpty && data.first is Map) {
        return ReviewsAggregate.fromJson(
          Map<String, Object?>.from(data.first as Map),
        );
      }
      return null;
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }
}
