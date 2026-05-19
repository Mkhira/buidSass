import 'package:dio/dio.dart';

import '../../../core/error/error_mapper.dart';
import 'inventory_gateway.dart';
import 'models/inventory_models.dart';

class InventoryGatewayImpl implements InventoryGateway {
  InventoryGatewayImpl({required Dio dio, ErrorMapper? errorMapper})
      : _dio = dio,
        _errors = errorMapper ?? const ErrorMapper();

  static const _path = '/v1/customer/inventory/availability';

  final Dio _dio;
  final ErrorMapper _errors;

  @override
  Future<List<InventoryAvailability>> getAvailability({
    required List<String> productIds,
    required String market,
  }) async {
    if (productIds.isEmpty) return const [];
    try {
      final res = await _dio.get<Object?>(
        _path,
        queryParameters: {
          'productIds': productIds.join(','),
          'market': market,
        },
      );
      final data = res.data;
      if (data is! List) return const [];
      return data
          .whereType<Map>()
          .map((m) =>
              InventoryAvailability.fromJson(Map<String, Object?>.from(m)))
          .toList(growable: false);
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }
}
