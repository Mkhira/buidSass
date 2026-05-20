import 'dart:typed_data';

import 'package:dio/dio.dart';

import '../../../core/error/error_mapper.dart';
import 'invoices_gateway.dart';
import 'models/invoice_models.dart';

/// Dio-backed [InvoicesGateway].
///
/// The PDF call uses `ResponseType.bytes` so dio doesn't try to decode
/// the binary body as JSON. We surface 404 + HTTP errors via the shared
/// [ErrorMapper] so the bloc maps them to the same `InvoiceUnavailable`
/// state as a missing JSON preview.
class InvoicesGatewayImpl implements InvoicesGateway {
  InvoicesGatewayImpl({required Dio dio, ErrorMapper? errorMapper})
      : _dio = dio,
        _errors = errorMapper ?? const ErrorMapper();

  static const _root = '/v1/customer/orders';

  final Dio _dio;
  final ErrorMapper _errors;

  @override
  Future<InvoicePreview> getPreview(String orderId) async {
    try {
      final res = await _dio.get<Object?>('$_root/$orderId/invoice');
      final raw = res.data;
      if (raw is! Map) {
        throw DioException(
          requestOptions: RequestOptions(path: 'invoice'),
          type: DioExceptionType.badResponse,
          error: 'Malformed invoice payload',
        );
      }
      return InvoicePreview.fromJson(Map<String, Object?>.from(raw));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<Uint8List> getPdf(String orderId) async {
    try {
      final res = await _dio.get<List<int>>(
        '$_root/$orderId/invoice.pdf',
        options: Options(responseType: ResponseType.bytes),
      );
      final raw = res.data;
      if (raw == null || raw.isEmpty) {
        throw DioException(
          requestOptions: RequestOptions(path: 'invoice.pdf'),
          type: DioExceptionType.badResponse,
          error: 'Empty PDF body',
        );
      }
      return Uint8List.fromList(raw);
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }
}
