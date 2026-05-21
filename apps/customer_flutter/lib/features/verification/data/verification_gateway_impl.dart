import 'dart:typed_data';

import 'package:dio/dio.dart';

import '../../../core/api/idempotency_interceptor.dart';
import '../../../core/error/error_mapper.dart';
import 'models/verification_models.dart';
import 'verification_gateway.dart';

/// Dio-backed [VerificationGateway]. Idempotency-Key headers are routed
/// through [IdempotencyInterceptor] via `RequestOptions.extra`, matching
/// the returns/checkout pattern.
class VerificationGatewayImpl implements VerificationGateway {
  VerificationGatewayImpl({required Dio dio, ErrorMapper? errorMapper})
      : _dio = dio,
        _errors = errorMapper ?? const ErrorMapper();

  static const _root = '/api/customer/verifications';

  final Dio _dio;
  final ErrorMapper _errors;

  @override
  Future<VerificationListPage> list() async {
    try {
      final res = await _dio.get<Object?>(_root);
      return VerificationListPage.fromJson(_asMap(res.data, _root));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<VerificationActive> getActive() async {
    try {
      final res = await _dio.get<Object?>('$_root/active');
      return VerificationActive.fromJson(_asMap(res.data, 'verifications/active'));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<VerificationSchema> getSchema() async {
    try {
      final res = await _dio.get<Object?>('$_root/schema');
      return VerificationSchema.fromJson(_asMap(res.data, 'verifications/schema'));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<VerificationDetail> getById(String id) async {
    final encoded = Uri.encodeComponent(id);
    try {
      final res = await _dio.get<Object?>('$_root/$encoded');
      return VerificationDetail.fromJson(
        _asMap(res.data, 'verifications/$encoded'),
      );
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<SubmitVerificationResult> submit({
    required SubmitVerificationRequest request,
    required String idempotencyKey,
  }) async {
    try {
      final res = await _dio.post<Object?>(
        _root,
        data: request.toJson(),
        options: Options(
          extra: {IdempotencyInterceptor.extraKey: idempotencyKey},
        ),
      );
      return SubmitVerificationResult.fromJson(
        _asMap(res.data, 'verifications/submit'),
      );
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<DocumentUploadResult> uploadDocument({
    required String verificationId,
    required String slotKey,
    required Uint8List bytes,
    required String filename,
  }) async {
    final encoded = Uri.encodeComponent(verificationId);
    try {
      final form = FormData.fromMap({
        'slotKey': slotKey,
        'file': MultipartFile.fromBytes(bytes, filename: filename),
      });
      final res = await _dio.post<Object?>(
        '$_root/$encoded/documents',
        data: form,
      );
      return DocumentUploadResult.fromJson(
        _asMap(res.data, 'verifications/$encoded/documents'),
      );
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<VerificationDetail> resubmit({
    required String verificationId,
    required ResubmitVerificationRequest request,
    required String idempotencyKey,
  }) async {
    final encoded = Uri.encodeComponent(verificationId);
    try {
      final res = await _dio.post<Object?>(
        '$_root/$encoded/resubmit',
        data: request.toJson(),
        options: Options(
          extra: {IdempotencyInterceptor.extraKey: idempotencyKey},
        ),
      );
      return VerificationDetail.fromJson(
        _asMap(res.data, 'verifications/$encoded/resubmit'),
      );
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<SubmitVerificationResult> renew({
    required RenewVerificationRequest request,
    required String idempotencyKey,
  }) async {
    try {
      final res = await _dio.post<Object?>(
        '$_root/renew',
        data: request.toJson(),
        options: Options(
          extra: {IdempotencyInterceptor.extraKey: idempotencyKey},
        ),
      );
      return SubmitVerificationResult.fromJson(
        _asMap(res.data, 'verifications/renew'),
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
