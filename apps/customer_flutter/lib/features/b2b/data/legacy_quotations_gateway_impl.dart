import 'package:dio/dio.dart';

import '../../../core/api/idempotency_interceptor.dart';
import '../../../core/error/error_mapper.dart';
import '../../../core/error/failure.dart';
import 'legacy_quotations_gateway.dart';
import 'models/legacy_quotation_models.dart';

class LegacyQuotationsGatewayImpl implements LegacyQuotationsGateway {
  LegacyQuotationsGatewayImpl({required Dio dio, ErrorMapper? errorMapper})
      : _dio = dio,
        _errors = errorMapper ?? const ErrorMapper();

  static const _root = '/v1/customer/quotations';

  final Dio _dio;
  final ErrorMapper _errors;

  @override
  Future<List<LegacyQuotationListItem>> list() async {
    try {
      final res = await _dio.get<Object?>(_root);
      final raw = res.data;
      // The server has two flavours — bare array or `{items:[...]}`.
      // Accept either so a future API tweak doesn't break the list
      // screen.
      // The server has two flavours — bare array or `{items:[...]}`.
      // Use a type check on the wrapped value too so a malformed
      // `items: <not-a-list>` body doesn't `TypeError` past the
      // graceful-fallback path.
      List<Object?> items;
      if (raw is List) {
        items = raw;
      } else if (raw is Map && raw['items'] is List) {
        items = raw['items'] as List;
      } else {
        items = const <Object?>[];
      }
      return items
          .whereType<Map>()
          .map(
            (m) =>
                LegacyQuotationListItem.fromJson(Map<String, Object?>.from(m)),
          )
          .toList(growable: false);
    } on DioException catch (e) {
      // BR-8: migrated accounts may 404 on this endpoint — return an
      // empty list so the menu entry is hidden cleanly instead of
      // bubbling a failure to the screen.
      final mapped = _errors.fromDio(e);
      if (mapped is NotFoundFailure) return const [];
      throw mapped;
    }
  }

  @override
  Future<LegacyQuotationDetail> getById(String id) async {
    final encoded = Uri.encodeComponent(id);
    try {
      final res = await _dio.get<Object?>('$_root/$encoded');
      return LegacyQuotationDetail.fromJson(
        _asMap(res.data, 'quotations/$encoded'),
      );
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<LegacyQuotationDetail> accept({
    required String id,
    required LegacyQuotationActionRequest request,
    required String idempotencyKey,
  }) =>
      _action(id, 'accept', request.toJson(), idempotencyKey);

  @override
  Future<LegacyQuotationDetail> reject({
    required String id,
    required LegacyQuotationActionRequest request,
    required String idempotencyKey,
  }) =>
      _action(id, 'reject', request.toJson(), idempotencyKey);

  Future<LegacyQuotationDetail> _action(
    String id,
    String segment,
    Map<String, Object?> body,
    String idempotencyKey,
  ) async {
    final encoded = Uri.encodeComponent(id);
    try {
      final res = await _dio.post<Object?>(
        '$_root/$encoded/$segment',
        data: body,
        options: Options(
          extra: {IdempotencyInterceptor.extraKey: idempotencyKey},
        ),
      );
      return LegacyQuotationDetail.fromJson(
        _asMap(res.data, 'quotations/$encoded/$segment'),
      );
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
