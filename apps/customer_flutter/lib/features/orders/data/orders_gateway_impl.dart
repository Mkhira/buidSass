import 'package:dio/dio.dart';

import '../../../core/error/error_mapper.dart';
import 'models/order_models.dart';
import 'orders_gateway.dart';

class OrdersGatewayImpl implements OrdersGateway {
  OrdersGatewayImpl({required Dio dio, ErrorMapper? errorMapper})
      : _dio = dio,
        _errors = errorMapper ?? const ErrorMapper();

  static const _root = '/v1/customer/orders';

  final Dio _dio;
  final ErrorMapper _errors;

  @override
  Future<OrderListPage> list(OrdersListFilter filter) async {
    try {
      final res =
          await _dio.get<Object?>(_root, queryParameters: filter.toQuery());
      final raw = res.data;
      if (raw is! Map) {
        throw DioException(
          requestOptions: RequestOptions(path: _root),
          type: DioExceptionType.badResponse,
          error: 'Malformed orders list payload',
        );
      }
      return OrderListPage.fromJson(Map<String, Object?>.from(raw));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<OrderDetail> getById(String orderId) async {
    try {
      final res = await _dio.get<Object?>('$_root/$orderId');
      return _decodeDetail(res.data, 'detail');
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<OrderDetail> cancel({
    required String orderId,
    required CancelOrderRequest request,
  }) async {
    try {
      final res = await _dio.post<Object?>(
        '$_root/$orderId/cancel',
        data: request.toJson(),
      );
      return _decodeDetail(res.data, 'cancel');
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<ReorderResult> reorder(String orderId) async {
    try {
      final res = await _dio.post<Object?>('$_root/$orderId/reorder');
      final raw = res.data;
      if (raw is! Map) {
        throw DioException(
          requestOptions: RequestOptions(path: 'reorder'),
          type: DioExceptionType.badResponse,
          error: 'Malformed reorder payload',
        );
      }
      return ReorderResult.fromJson(Map<String, Object?>.from(raw));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<ReturnEligibility> getReturnEligibility(String orderId) async {
    try {
      final res = await _dio.get<Object?>('$_root/$orderId/return-eligibility');
      final raw = res.data;
      if (raw is! Map) {
        throw DioException(
          requestOptions: RequestOptions(path: 'return-eligibility'),
          type: DioExceptionType.badResponse,
          error: 'Malformed return-eligibility payload',
        );
      }
      return ReturnEligibility.fromJson(Map<String, Object?>.from(raw));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  OrderDetail _decodeDetail(Object? raw, String label) {
    if (raw is! Map) {
      throw DioException(
        requestOptions: RequestOptions(path: label),
        type: DioExceptionType.badResponse,
        error: 'Malformed $label payload',
      );
    }
    return OrderDetail.fromJson(Map<String, Object?>.from(raw));
  }
}
