import 'package:customer_flutter/core/error/failure.dart';
import 'package:customer_flutter/features/auth/data/auth_repository_impl.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

/// Intercepts every outbound request and resolves it with a canned response
/// (or a canned [DioException]). Avoids pulling in `http_mock_adapter` —
/// the existing test suite uses the same hand-rolled style.
class _StubInterceptor extends Interceptor {
  _StubInterceptor(this.handler);

  /// Receives `(method, path, data)` and returns either:
  ///  * `Response` for a 2xx body, OR
  ///  * `DioException` for an error path.
  final Object? Function(String method, String path, Object? data) handler;

  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler h) {
    final result = handler(options.method, options.path, options.data);
    if (result is Response) {
      h.resolve(Response<Object?>(
        requestOptions: options,
        statusCode: result.statusCode ?? 200,
        data: result.data,
      ));
      return;
    }
    if (result is DioException) {
      h.reject(DioException(
        requestOptions: options,
        type: result.type,
        response: result.response == null
            ? null
            : Response<Object?>(
                requestOptions: options,
                statusCode: result.response!.statusCode,
                data: result.response!.data,
              ),
      ));
      return;
    }
    h.resolve(Response<Object?>(
      requestOptions: options,
      statusCode: 200,
      data: result,
    ));
  }
}

Dio buildStubbedDio(
  Object? Function(String method, String path, Object? data) handler,
) {
  final dio = Dio(BaseOptions(baseUrl: 'https://example.test'));
  dio.interceptors.add(_StubInterceptor(handler));
  return dio;
}

void main() {
  group('login', () {
    test('2xx → AuthOutcome.success with tokens + profile', () async {
      final dio = buildStubbedDio((m, p, d) => Response<Map<String, Object?>>(
            requestOptions: RequestOptions(path: p),
            statusCode: 200,
            data: const {
              'accountId': 'acc-1',
              'accessToken': 'at',
              'refreshToken': 'rt',
              'expiresInSeconds': 900,
              'mfaRequired': false,
              'profile': {
                'displayName': 'Ahmed',
                'email': 'a@b.com',
                'locale': 'ar',
                'marketCode': 'SA',
                'emailConfirmed': true,
              },
            },
          ));
      final repo = AuthRepositoryImpl(dio: dio);
      final outcome = await repo.login(email: 'a@b.com', password: 'secret');
      expect(outcome.ok, isTrue);
      expect(outcome.accessToken, 'at');
      expect(outcome.refreshToken, 'rt');
      expect(outcome.customerId, 'acc-1');
      expect(outcome.email, 'a@b.com');
      expect(outcome.displayName, 'Ahmed');
      expect(outcome.isVerified, isTrue);
    });

    test('mfaRequired=true → AuthOutcome.requiresOtp', () async {
      final dio = buildStubbedDio((m, p, d) => Response<Map<String, Object?>>(
            requestOptions: RequestOptions(path: p),
            statusCode: 200,
            data: const {
              'accountId': 'acc-1',
              'accessToken': 'at',
              'refreshToken': 'rt',
              'mfaRequired': true,
              'mfaChallengeId': 'mfa-1',
              'mfaChannel': 'sms',
              'mfaResendInSeconds': 30,
              'profile': {},
            },
          ));
      final repo = AuthRepositoryImpl(dio: dio);
      final outcome = await repo.login(email: 'a@b.com', password: 'secret');
      expect(outcome.otpChallenge, isNotNull);
      expect(outcome.otpChallenge!.challengeId, 'mfa-1');
      expect(outcome.otpChallenge!.channel, 'sms');
    });

    test('401 → AuthOutcome.failure(reasonCode=identity.bad_credentials)',
        () async {
      final dio = buildStubbedDio((m, p, d) {
        return DioException(
          requestOptions: RequestOptions(path: p),
          type: DioExceptionType.badResponse,
          response: Response<Object?>(
            requestOptions: RequestOptions(path: p),
            statusCode: 401,
            data: const {
              'error': {
                'code': 'auth.bad_credentials',
                'message': 'Invalid credentials',
                'correlationId': 'corr-1',
              },
            },
          ),
        );
      });
      final repo = AuthRepositoryImpl(dio: dio);
      final outcome = await repo.login(email: 'x', password: 'y');
      expect(outcome.ok, isFalse);
      expect(outcome.reasonCode, 'identity.bad_credentials');
    });

    test('429 → identity.rate_limited', () async {
      final dio = buildStubbedDio((m, p, d) {
        return DioException(
          requestOptions: RequestOptions(path: p),
          type: DioExceptionType.badResponse,
          response: Response<Object?>(
            requestOptions: RequestOptions(path: p),
            statusCode: 429,
            data: const {
              'error': {
                'code': 'rate.limited',
                'message': 'Slow down',
                'correlationId': 'c-2',
                'details': {'retryAfterSeconds': 30},
              },
            },
          ),
        );
      });
      final repo = AuthRepositoryImpl(dio: dio);
      final outcome = await repo.login(email: 'x', password: 'y');
      expect(outcome.reasonCode, 'identity.rate_limited');
    });

    test('connectionError → network.offline', () async {
      final dio = buildStubbedDio((m, p, d) {
        return DioException(
          requestOptions: RequestOptions(path: p),
          type: DioExceptionType.connectionError,
        );
      });
      final repo = AuthRepositoryImpl(dio: dio);
      final outcome = await repo.login(email: 'x', password: 'y');
      expect(outcome.reasonCode, 'network.offline');
    });
  });

  group('register', () {
    test('201 with otpChallengeId → AuthOutcome.requiresOtp', () async {
      final dio = buildStubbedDio((m, p, d) => Response<Map<String, Object?>>(
            requestOptions: RequestOptions(path: p),
            statusCode: 201,
            data: const {
              'accountId': 'acc-2',
              'otpChallengeId': 'otp-1',
              'otpChannel': 'sms',
              'otpResendInSeconds': 30,
            },
          ));
      final repo = AuthRepositoryImpl(dio: dio);
      final outcome = await repo.register(
        email: 'a@b.com',
        password: 'p',
        displayName: 'A',
      );
      expect(outcome.otpChallenge?.challengeId, 'otp-1');
      expect(outcome.otpChallenge?.channel, 'sms');
    });

    test('409 (duplicate) → identity.conflict', () async {
      final dio = buildStubbedDio((m, p, d) => DioException(
            requestOptions: RequestOptions(path: p),
            type: DioExceptionType.badResponse,
            response: Response<Object?>(
              requestOptions: RequestOptions(path: p),
              statusCode: 409,
              data: const {
                'error': {
                  'code': 'identity.duplicate',
                  'message': 'Email exists',
                  'correlationId': 'c-3',
                },
              },
            ),
          ));
      final repo = AuthRepositoryImpl(dio: dio);
      final outcome = await repo.register(
        email: 'a@b.com',
        password: 'p',
        displayName: 'A',
      );
      expect(outcome.reasonCode, 'identity.conflict');
    });
  });

  group('requestOtp / resendOtp', () {
    test('happy path returns OtpChallenge', () async {
      final dio = buildStubbedDio((m, p, d) => Response<Map<String, Object?>>(
            requestOptions: RequestOptions(path: p),
            statusCode: 200,
            data: const {
              'otpChallengeId': 'otp-2',
              'channel': 'sms',
              'resendAvailableInSeconds': 60,
            },
          ));
      final repo = AuthRepositoryImpl(dio: dio);
      final c = await repo.requestOtp(identifier: 'a@b.com');
      expect(c.challengeId, 'otp-2');
      expect(c.retryAfterSeconds, 60);
    });

    test('429 throws Failure (ValidationFailure with retryAfterSeconds)',
        () async {
      final dio = buildStubbedDio((m, p, d) => DioException(
            requestOptions: RequestOptions(path: p),
            type: DioExceptionType.badResponse,
            response: Response<Object?>(
              requestOptions: RequestOptions(path: p),
              statusCode: 429,
              data: const {
                'error': {
                  'code': 'rate.limited',
                  'message': 'Slow down',
                  'correlationId': 'c-4',
                  'details': {'retryAfterSeconds': 45},
                },
              },
            ),
          ));
      final repo = AuthRepositoryImpl(dio: dio);
      await expectLater(
        () => repo.requestOtp(identifier: 'a@b.com'),
        throwsA(isA<ValidationFailure>()
            .having((f) => f.retryAfterSeconds, 'retryAfterSeconds', 45)),
      );
    });
  });

  group('verifyOtp', () {
    test('2xx → AuthOutcome.success', () async {
      final dio = buildStubbedDio((m, p, d) => Response<Map<String, Object?>>(
            requestOptions: RequestOptions(path: p),
            statusCode: 200,
            data: const {
              'accountId': 'acc-3',
              'accessToken': 'at',
              'refreshToken': 'rt',
              'profile': {'displayName': 'X'},
            },
          ));
      final repo = AuthRepositoryImpl(dio: dio);
      final outcome =
          await repo.verifyOtp(challengeId: 'otp-1', code: '123456');
      expect(outcome.ok, isTrue);
      expect(outcome.accessToken, 'at');
    });

    test('410 (expired) → identity.validation', () async {
      final dio = buildStubbedDio((m, p, d) => DioException(
            requestOptions: RequestOptions(path: p),
            type: DioExceptionType.badResponse,
            response: Response<Object?>(
              requestOptions: RequestOptions(path: p),
              statusCode: 410,
              data: const {
                'error': {
                  'code': 'otp.expired',
                  'message': 'Expired',
                  'correlationId': 'c-5',
                },
              },
            ),
          ));
      final repo = AuthRepositoryImpl(dio: dio);
      final outcome =
          await repo.verifyOtp(challengeId: 'otp-1', code: '000000');
      expect(outcome.reasonCode, 'identity.validation');
    });
  });

  group('password reset', () {
    test('requestPasswordReset 2xx completes', () async {
      final dio = buildStubbedDio((m, p, d) => Response<void>(
            requestOptions: RequestOptions(path: p),
            statusCode: 200,
          ));
      final repo = AuthRepositoryImpl(dio: dio);
      await expectLater(
        repo.requestPasswordReset(email: 'a@b.com'),
        completes,
      );
    });

    test('confirmPasswordReset with "challengeId:code" splits and sends both',
        () async {
      Map<String, Object?>? capturedBody;
      final dio = buildStubbedDio((m, p, d) {
        if (d is Map) capturedBody = d.cast<String, Object?>();
        return Response<void>(
          requestOptions: RequestOptions(path: p),
          statusCode: 200,
        );
      });
      final repo = AuthRepositoryImpl(dio: dio);
      await repo.confirmPasswordReset(
        token: 'challenge-1:123456',
        newPassword: 'newPass',
      );
      expect(capturedBody!['resetChallengeId'], 'challenge-1');
      expect(capturedBody!['code'], '123456');
      expect(capturedBody!['newPassword'], 'newPass');
    });

    test('confirmPasswordReset with single token sends token field', () async {
      Map<String, Object?>? capturedBody;
      final dio = buildStubbedDio((m, p, d) {
        if (d is Map) capturedBody = d.cast<String, Object?>();
        return Response<void>(
          requestOptions: RequestOptions(path: p),
          statusCode: 200,
        );
      });
      final repo = AuthRepositoryImpl(dio: dio);
      await repo.confirmPasswordReset(
        token: 'opaque-token',
        newPassword: 'newPass',
      );
      expect(capturedBody!['token'], 'opaque-token');
      expect(capturedBody!.containsKey('resetChallengeId'), isFalse);
    });
  });
}
