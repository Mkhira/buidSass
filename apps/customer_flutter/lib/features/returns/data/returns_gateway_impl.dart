import 'dart:typed_data';

import 'package:dio/dio.dart';

import '../../../core/api/idempotency_interceptor.dart';
import '../../../core/error/error_mapper.dart';
import 'models/return_models.dart';
import 'returns_gateway.dart';

/// Dio-backed [ReturnsGateway]. Idempotency-Key headers are routed
/// through [IdempotencyInterceptor] via `RequestOptions.extra`, matching
/// the checkout-submit pattern from Phase 4.
class ReturnsGatewayImpl implements ReturnsGateway {
  ReturnsGatewayImpl({required Dio dio, ErrorMapper? errorMapper})
      : _dio = dio,
        _errors = errorMapper ?? const ErrorMapper();

  static const _root = '/v1/customer/returns';
  static const _photos = '/v1/customer/returns/photos';
  static const _ordersRoot = '/v1/customer/orders';

  final Dio _dio;
  final ErrorMapper _errors;

  @override
  Future<ReturnListPage> list(ReturnsListFilter filter) async {
    try {
      final res =
          await _dio.get<Object?>(_root, queryParameters: filter.toQuery());
      return ReturnListPage.fromJson(_asMap(res.data, _root));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<ReturnDetail> getById(String id) async {
    // Encode the path segment — uuid IDs are URL-safe today, but the
    // gateway contract accepts arbitrary strings so we defend against
    // reserved characters (CodeRabbit feedback). Same fix for the
    // orders-root path in `create` below.
    final encodedId = Uri.encodeComponent(id);
    try {
      final res = await _dio.get<Object?>('$_root/$encodedId');
      return ReturnDetail.fromJson(_asMap(res.data, 'returns/$encodedId'));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<ReturnPhotoUploadResult> uploadPhoto({
    required Uint8List bytes,
    required String filename,
    required String clientPhotoKey,
  }) async {
    try {
      final form = FormData.fromMap({
        // Echo the key in the multipart body too — spec.md S-6.2 §
        // "Idempotency-Key strategy" + data-model.md POST /photos.
        'clientPhotoKey': clientPhotoKey,
        'file': MultipartFile.fromBytes(bytes, filename: filename),
      });
      final res = await _dio.post<Object?>(
        _photos,
        data: form,
        options: Options(
          // IdempotencyInterceptor lifts this onto the
          // `Idempotency-Key` header — matches checkout-submit wiring.
          extra: {IdempotencyInterceptor.extraKey: clientPhotoKey},
        ),
      );
      return ReturnPhotoUploadResult.fromJson(_asMap(res.data, _photos));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<CreateReturnResult> create({
    required String orderId,
    required CreateReturnRequest request,
    required String idempotencyKey,
  }) async {
    final encodedOrderId = Uri.encodeComponent(orderId);
    try {
      final res = await _dio.post<Object?>(
        '$_ordersRoot/$encodedOrderId/returns',
        data: request.toJson(),
        options: Options(
          extra: {IdempotencyInterceptor.extraKey: idempotencyKey},
        ),
      );
      return CreateReturnResult.fromJson(_asMap(res.data, 'returns/create'));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  Map<String, Object?> _asMap(Object? raw, String label) {
    if (raw is! Map) {
      throw DioException(
        requestOptions: RequestOptions(path: label),
        type: DioExceptionType.badResponse,
        error: 'Malformed $label payload',
      );
    }
    return Map<String, Object?>.from(raw);
  }
}
