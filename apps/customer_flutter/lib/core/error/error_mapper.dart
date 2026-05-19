import 'package:dio/dio.dart';

import 'failure.dart';

/// Maps a [DioException] (or arbitrary thrown object) into a typed
/// [Failure]. The mapping is deterministic and consults, in order:
///
/// 1. `DioExceptionType` for network/timeout/cancel/offline classification.
/// 2. The response status code for HTTP variants.
/// 3. The response body's `error.code` for fine-grained typing within the
///    same status code (e.g. `auth.bad_credentials` vs `auth.account_locked`
///    both arrive as 401).
///
/// The correlation id is read from `response.data.error.correlationId`
/// with fallback to the `X-Correlation-Id` request header (which the
/// [`CorrelationIdInterceptor`] always sets), guaranteeing every Failure
/// has a non-empty correlation id for surfacing in error UI.
class ErrorMapper {
  const ErrorMapper();

  Failure fromDio(DioException e) {
    final correlationId = _correlationId(e);

    // ----- transport-level conditions (no usable response) -----
    switch (e.type) {
      case DioExceptionType.cancel:
        return NetworkFailure(
          code: 'network.cancelled',
          message: 'Request was cancelled',
          correlationId: correlationId,
        );
      case DioExceptionType.connectionTimeout:
      case DioExceptionType.sendTimeout:
      case DioExceptionType.receiveTimeout:
        return NetworkFailure(
          code: 'network.timeout',
          message: 'The server took too long to respond',
          correlationId: correlationId,
        );
      case DioExceptionType.connectionError:
        return OfflineFailure(
          code: 'network.offline',
          message: 'No network connection',
          correlationId: correlationId,
        );
      case DioExceptionType.badCertificate:
        return NetworkFailure(
          code: 'network.bad_certificate',
          message: 'Could not verify the server certificate',
          correlationId: correlationId,
        );
      case DioExceptionType.unknown:
        if (e.response == null) {
          return OfflineFailure(
            code: 'network.unknown',
            message: 'Network is unreachable',
            correlationId: correlationId,
          );
        }
        break;
      case DioExceptionType.badResponse:
        break;
    }

    // ----- HTTP response with body -----
    final response = e.response;
    if (response == null) {
      return ServerFailure(
        code: 'server.no_response',
        message: 'No response from server',
        correlationId: correlationId,
      );
    }
    return _fromResponse(
      statusCode: response.statusCode ?? 0,
      data: response.data,
      correlationId: correlationId,
    );
  }

  /// Map a raw status code + JSON body to a [Failure]. Public so callers
  /// that bypass Dio (e.g. file uploads via plain HttpClient) can reuse the
  /// same mapping.
  Failure fromResponse({
    required int statusCode,
    required Object? data,
    required String correlationId,
  }) =>
      _fromResponse(
        statusCode: statusCode,
        data: data,
        correlationId: correlationId,
      );

  /// Last-resort mapping for non-Dio exceptions (e.g. JSON parse).
  Failure fromUnknown(Object error, {String? correlationId}) {
    if (error is DioException) return fromDio(error);
    return ServerFailure(
      code: 'server.unknown',
      message: error.toString(),
      correlationId: correlationId ?? '',
    );
  }

  Failure _fromResponse({
    required int statusCode,
    required Object? data,
    required String correlationId,
  }) {
    final body = _readErrorBody(data);
    final code = body.code ?? 'http.$statusCode';
    final message = body.message ?? 'Request failed ($statusCode)';
    final details = body.details;

    if (statusCode >= 500) {
      return ServerFailure(
        code: code,
        message: message,
        correlationId: correlationId,
        details: details,
      );
    }
    switch (statusCode) {
      case 400:
        return ValidationFailure(
          code: code,
          message: message,
          correlationId: correlationId,
          fields: _readFields(details),
          retryAfterSeconds: _readRetryAfter(details),
          details: details,
        );
      case 401:
        return AuthFailure(
          code: code,
          message: message,
          correlationId: correlationId,
          details: details,
        );
      case 403:
        return ForbiddenFailure(
          code: code,
          message: message,
          correlationId: correlationId,
          details: details,
        );
      case 404:
        return NotFoundFailure(
          code: code,
          message: message,
          correlationId: correlationId,
          details: details,
        );
      case 409:
        return ConflictFailure(
          code: code,
          message: message,
          correlationId: correlationId,
          details: details,
        );
      case 410:
        // Expired token (OTP, password-reset, email-confirm). Fold into
        // ValidationFailure with subkind=expired so screens that key on
        // 422 fields can reuse the same handler.
        return ValidationFailure(
          code: code,
          message: message,
          correlationId: correlationId,
          retryAfterSeconds: _readRetryAfter(details),
          details: details,
        );
      case 422:
        return ValidationFailure(
          code: code,
          message: message,
          correlationId: correlationId,
          fields: _readFields(details),
          retryAfterSeconds: _readRetryAfter(details),
          details: details,
        );
      case 429:
        return ValidationFailure(
          code: code,
          message: message,
          correlationId: correlationId,
          retryAfterSeconds: _readRetryAfter(details),
          details: details,
        );
      default:
        return ServerFailure(
          code: code,
          message: message,
          correlationId: correlationId,
          details: details,
        );
    }
  }

  String _correlationId(DioException e) {
    final body = _readErrorBody(e.response?.data);
    if (body.correlationId != null && body.correlationId!.isNotEmpty) {
      return body.correlationId!;
    }
    final fromHeader =
        e.requestOptions.headers['X-Correlation-Id']?.toString() ??
            e.requestOptions.headers['x-correlation-id']?.toString();
    return fromHeader ?? '';
  }

  _ErrorBody _readErrorBody(Object? data) {
    if (data is! Map) return const _ErrorBody();
    final error = data['error'];
    if (error is! Map) return const _ErrorBody();
    return _ErrorBody(
      code: error['code']?.toString(),
      message: error['message']?.toString(),
      correlationId: error['correlationId']?.toString(),
      details: error['details'] is Map
          ? Map<String, Object?>.from(error['details'] as Map)
          : null,
    );
  }

  List<ValidationFieldError> _readFields(Map<String, Object?>? details) {
    if (details == null) return const [];
    final raw = details['fields'];
    if (raw is! List) return const [];
    return raw
        .whereType<Map>()
        .map((e) => ValidationFieldError(
              path: e['path']?.toString() ?? '',
              message: e['message']?.toString() ?? '',
              code: e['code']?.toString(),
            ))
        .toList(growable: false);
  }

  int? _readRetryAfter(Map<String, Object?>? details) {
    if (details == null) return null;
    final raw = details['retryAfterSeconds'];
    if (raw is num) return raw.toInt();
    if (raw is String) return int.tryParse(raw);
    return null;
  }
}

class _ErrorBody {
  const _ErrorBody({
    this.code,
    this.message,
    this.correlationId,
    this.details,
  });
  final String? code;
  final String? message;
  final String? correlationId;
  final Map<String, Object?>? details;
}
