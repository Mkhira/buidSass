import 'package:dio/dio.dart';

import '../../../core/error/error_mapper.dart';
import 'models/pricing_models.dart';
import 'pricing_gateway.dart';

class PricingGatewayImpl implements PricingGateway {
  PricingGatewayImpl({required Dio dio, ErrorMapper? errorMapper})
      : _dio = dio,
        _errors = errorMapper ?? const ErrorMapper();

  static const _previewPath = '/customer/pricing/price-cart';

  final Dio _dio;
  final ErrorMapper _errors;

  @override
  Future<PriceQuote> preview(PricingRequest request) async {
    try {
      final res = await _dio.post<Object?>(
        _previewPath,
        data: request.toJson(),
      );
      final body = res.data;
      if (body is! Map) {
        throw DioException(
          requestOptions: RequestOptions(path: _previewPath),
          type: DioExceptionType.unknown,
          error: 'Malformed pricing response',
        );
      }
      return PriceQuote.fromJson(Map<String, Object?>.from(body));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }
}
