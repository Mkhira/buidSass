import 'package:customer_flutter/core/error/error_mapper.dart';
import 'package:customer_flutter/core/error/failure.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  const mapper = ErrorMapper();

  RequestOptions buildOptions({String? correlationId}) => RequestOptions(
        path: '/v1/customer/identity/sign-in',
        headers: {
          if (correlationId != null) 'X-Correlation-Id': correlationId,
        },
      );

  DioException badResponse({
    required int status,
    Object? body,
    String? correlationId,
  }) {
    final opts = buildOptions(correlationId: correlationId);
    return DioException(
      requestOptions: opts,
      type: DioExceptionType.badResponse,
      response: Response<Object?>(
        requestOptions: opts,
        statusCode: status,
        data: body,
      ),
    );
  }

  group('transport-level mapping', () {
    test('cancel → NetworkFailure(code=network.cancelled)', () {
      final f = mapper.fromDio(DioException(
        requestOptions: buildOptions(correlationId: 'corr-cancel'),
        type: DioExceptionType.cancel,
      ));
      expect(f, isA<NetworkFailure>());
      expect(f.code, 'network.cancelled');
      expect(f.correlationId, 'corr-cancel');
    });

    test('connectionTimeout → NetworkFailure(code=network.timeout)', () {
      final f = mapper.fromDio(DioException(
        requestOptions: buildOptions(correlationId: 'corr-timeout'),
        type: DioExceptionType.connectionTimeout,
      ));
      expect(f, isA<NetworkFailure>());
      expect(f.code, 'network.timeout');
    });

    test('connectionError → OfflineFailure', () {
      final f = mapper.fromDio(DioException(
        requestOptions: buildOptions(),
        type: DioExceptionType.connectionError,
      ));
      expect(f, isA<OfflineFailure>());
      expect(f.code, 'network.offline');
    });

    test('unknown without response → OfflineFailure', () {
      final f = mapper.fromDio(DioException(
        requestOptions: buildOptions(),
        type: DioExceptionType.unknown,
      ));
      expect(f, isA<OfflineFailure>());
    });
  });

  group('HTTP response → Failure', () {
    test('401 → AuthFailure with code/message/correlation from body', () {
      final f = mapper.fromDio(badResponse(
        status: 401,
        body: {
          'error': {
            'code': 'auth.bad_credentials',
            'message': 'Invalid credentials',
            'correlationId': 'corr-401',
          },
        },
      ));
      expect(f, isA<AuthFailure>());
      expect(f.code, 'auth.bad_credentials');
      expect(f.message, 'Invalid credentials');
      expect(f.correlationId, 'corr-401');
    });

    test('403 → ForbiddenFailure', () {
      final f = mapper.fromDio(badResponse(status: 403, body: {
        'error': {
          'code': 'identity.locked',
          'message': 'Account locked',
          'correlationId': 'corr-403',
        }
      }));
      expect(f, isA<ForbiddenFailure>());
    });

    test('409 → ConflictFailure', () {
      final f = mapper.fromDio(badResponse(status: 409));
      expect(f, isA<ConflictFailure>());
    });

    test('410 → ValidationFailure (expired)', () {
      final f = mapper.fromDio(badResponse(
        status: 410,
        body: {
          'error': {
            'code': 'otp.expired',
            'message': 'Code expired',
            'correlationId': 'corr-410',
          },
        },
      ));
      expect(f, isA<ValidationFailure>());
      expect(f.code, 'otp.expired');
    });

    test('422 → ValidationFailure with field list', () {
      final f = mapper.fromDio(badResponse(
        status: 422,
        body: {
          'error': {
            'code': 'validation.failed',
            'message': 'Invalid',
            'correlationId': 'corr-422',
            'details': {
              'fields': [
                {'path': 'password', 'message': 'Too short', 'code': 'min'},
              ],
            },
          },
        },
      )) as ValidationFailure;
      expect(f.fields, hasLength(1));
      expect(f.fields.first.path, 'password');
      expect(f.fields.first.code, 'min');
    });

    test('429 → ValidationFailure with retryAfterSeconds', () {
      final f = mapper.fromDio(badResponse(
        status: 429,
        body: {
          'error': {
            'code': 'rate.limited',
            'message': 'Slow down',
            'correlationId': 'corr-429',
            'details': {'retryAfterSeconds': 30},
          },
        },
      )) as ValidationFailure;
      expect(f.retryAfterSeconds, 30);
    });

    test('500 → ServerFailure', () {
      final f = mapper.fromDio(badResponse(status: 502));
      expect(f, isA<ServerFailure>());
    });

    test('unknown 418 status → ServerFailure fallback', () {
      final f = mapper.fromDio(badResponse(status: 418));
      expect(f, isA<ServerFailure>());
    });
  });

  group('correlation id resolution', () {
    test('falls back to X-Correlation-Id header when body lacks one', () {
      final f = mapper.fromDio(badResponse(
        status: 500,
        body: {'error': {'code': 'server', 'message': 'x'}},
        correlationId: 'header-corr',
      ));
      expect(f.correlationId, 'header-corr');
    });

    test('correlationIdShort returns last 8 chars', () {
      const f = ServerFailure(
        code: 'x',
        message: 'x',
        correlationId: '0123456789abcdef',
      );
      expect(f.correlationIdShort, '89abcdef');
    });
  });

  test('fromUnknown(non-Dio) → ServerFailure', () {
    final f = mapper.fromUnknown(StateError('boom'));
    expect(f, isA<ServerFailure>());
    expect(f.code, 'server.unknown');
  });
}
