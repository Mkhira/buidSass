import 'dart:typed_data';

import 'package:dio/dio.dart';

import '../../../core/api/idempotency_interceptor.dart';
import '../../../core/error/error_mapper.dart';
import 'models/quote_models.dart';
import 'quotes_gateway.dart';

/// Dio-backed [QuotesGateway]. Idempotency-Key headers are routed
/// through [IdempotencyInterceptor] via `RequestOptions.extra`, matching
/// the returns / verification gateway pattern.
class QuotesGatewayImpl implements QuotesGateway {
  QuotesGatewayImpl({required Dio dio, ErrorMapper? errorMapper})
      : _dio = dio,
        _errors = errorMapper ?? const ErrorMapper();

  static const _root = '/api/customer/quotes';

  final Dio _dio;
  final ErrorMapper _errors;

  @override
  Future<QuotesPage> list(QuotesFilter filter) async {
    try {
      final res =
          await _dio.get<Object?>(_root, queryParameters: filter.toQuery());
      return QuotesPage.fromJson(_asMap(res.data, _root));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<QuotesPage> awaitingMyApproval() async {
    try {
      final res = await _dio.get<Object?>('$_root/awaiting-my-approval');
      return QuotesPage.fromJson(
        _asMap(res.data, 'quotes/awaiting-my-approval'),
      );
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<QuoteDetail> getById(String id) async {
    final encoded = Uri.encodeComponent(id);
    try {
      final res = await _dio.get<Object?>('$_root/$encoded');
      return QuoteDetail.fromJson(_asMap(res.data, 'quotes/$encoded'));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<CreateQuoteResult> createFromCart({
    required CreateQuoteFromCartRequest request,
    required String idempotencyKey,
  }) =>
      _create('$_root/from-cart', request.toJson(), idempotencyKey);

  @override
  Future<CreateQuoteResult> createFromProduct({
    required CreateQuoteFromProductRequest request,
    required String idempotencyKey,
  }) =>
      _create('$_root/from-product', request.toJson(), idempotencyKey);

  Future<CreateQuoteResult> _create(
    String path,
    Map<String, Object?> body,
    String idempotencyKey,
  ) async {
    try {
      final res = await _dio.post<Object?>(
        path,
        data: body,
        options: Options(
          extra: {IdempotencyInterceptor.extraKey: idempotencyKey},
        ),
      );
      return CreateQuoteResult.fromJson(_asMap(res.data, path));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<QuoteDetail> submitAcceptance({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  }) =>
      _action(quoteId, 'submit-acceptance', request.toJson(), idempotencyKey);

  @override
  Future<QuoteDetail> finalizeAcceptance({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  }) =>
      _action(quoteId, 'finalize-acceptance', request.toJson(), idempotencyKey);

  @override
  Future<QuoteDetail> rejectAcceptance({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  }) =>
      _action(quoteId, 'reject-acceptance', request.toJson(), idempotencyKey);

  @override
  Future<QuoteDetail> requestRevision({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  }) =>
      _action(quoteId, 'request-revision', request.toJson(), idempotencyKey);

  @override
  Future<QuoteDetail> withdraw({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  }) =>
      _action(quoteId, 'withdraw', request.toJson(), idempotencyKey);

  Future<QuoteDetail> _action(
    String quoteId,
    String segment,
    Map<String, Object?> body,
    String idempotencyKey,
  ) async {
    final encoded = Uri.encodeComponent(quoteId);
    try {
      final res = await _dio.post<Object?>(
        '$_root/$encoded/$segment',
        data: body,
        options: Options(
          extra: {IdempotencyInterceptor.extraKey: idempotencyKey},
        ),
      );
      return QuoteDetail.fromJson(_asMap(res.data, 'quotes/$encoded/$segment'));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<SaveAsTemplateResult> saveAsTemplate({
    required String quoteId,
    required SaveAsTemplateRequest request,
    required String idempotencyKey,
  }) async {
    final encoded = Uri.encodeComponent(quoteId);
    try {
      final res = await _dio.post<Object?>(
        '$_root/$encoded/save-as-template',
        data: request.toJson(),
        options: Options(
          extra: {IdempotencyInterceptor.extraKey: idempotencyKey},
        ),
      );
      return SaveAsTemplateResult.fromJson(
        _asMap(res.data, 'quotes/$encoded/save-as-template'),
      );
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<Uint8List> downloadDocument({
    required String quoteId,
    required String versionId,
    required String locale,
  }) async {
    final qid = Uri.encodeComponent(quoteId);
    final vid = Uri.encodeComponent(versionId);
    final loc = Uri.encodeComponent(locale);
    try {
      final res = await _dio.get<List<int>>(
        '$_root/$qid/versions/$vid/documents/$loc',
        options: Options(responseType: ResponseType.bytes),
      );
      final bytes = res.data;
      if (bytes == null) {
        throw DioException(
          requestOptions: res.requestOptions,
          type: DioExceptionType.badResponse,
          error: 'Empty document bytes',
        );
      }
      return Uint8List.fromList(bytes);
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
