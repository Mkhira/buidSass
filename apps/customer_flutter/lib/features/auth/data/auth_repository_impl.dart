import 'package:dio/dio.dart';

import '../../../core/error/error_mapper.dart';
import '../../../core/error/failure.dart';
import 'auth_repository.dart';

/// Real [AuthRepository] implementation that hits the 7 customer identity
/// endpoints currently exposed by the [AuthRepository] interface. Wired by
/// DI when `FeatureFlags.realIdentityClientShipped` is true; otherwise
/// [StubAuthRepository] is used.
///
/// Endpoints (per `services/backend_api/openapi.identity.json`):
///
/// * POST `/v1/customer/identity/sign-in`            → [login]
/// * POST `/v1/customer/identity/register`           → [register]
/// * POST `/v1/customer/identity/otp/request`        → [requestOtp] / [resendOtp]
/// * POST `/v1/customer/identity/otp/verify`         → [verifyOtp]
/// * POST `/v1/customer/identity/password/reset-request`  → [requestPasswordReset]
/// * POST `/v1/customer/identity/password/reset-complete` → [confirmPasswordReset]
///
/// All errors map to canonical reason codes via [_mapFailureToReasonCode].
/// The bloc layer keys on these codes for localized UX branching.
class AuthRepositoryImpl implements AuthRepository {
  AuthRepositoryImpl({required Dio dio, ErrorMapper? errorMapper})
      : _dio = dio,
        _errors = errorMapper ?? const ErrorMapper();

  final Dio _dio;
  final ErrorMapper _errors;

  static const _signInPath = '/v1/customer/identity/sign-in';
  static const _registerPath = '/v1/customer/identity/register';
  static const _otpRequestPath = '/v1/customer/identity/otp/request';
  static const _otpVerifyPath = '/v1/customer/identity/otp/verify';
  static const _passwordResetRequestPath =
      '/v1/customer/identity/password/reset-request';
  static const _passwordResetCompletePath =
      '/v1/customer/identity/password/reset-complete';

  @override
  Future<AuthOutcome> login({
    required String email,
    required String password,
  }) async {
    try {
      final res = await _dio.post<Map<String, Object?>>(
        _signInPath,
        data: {'identifier': email, 'password': password},
      );
      final body = res.data ?? const {};
      return _outcomeFromSessionBody(body);
    } on DioException catch (e) {
      final failure = _errors.fromDio(e);
      return AuthOutcome.failure(reasonCode: _mapFailureToReasonCode(failure));
    }
  }

  @override
  Future<AuthOutcome> register({
    required String email,
    required String password,
    required String displayName,
  }) async {
    try {
      final res = await _dio.post<Map<String, Object?>>(
        _registerPath,
        data: {
          'email': email,
          'password': password,
          'displayName': displayName,
          'termsAccepted': true,
        },
      );
      final body = res.data ?? const {};
      final otpChallengeId = body['otpChallengeId']?.toString();
      if (otpChallengeId != null && otpChallengeId.isNotEmpty) {
        return AuthOutcome.requiresOtp(
          otpChallenge: OtpChallenge(
            challengeId: otpChallengeId,
            channel: body['otpChannel']?.toString() ?? 'sms',
            retryAfterSeconds: _readInt(body['otpResendInSeconds']) ?? 30,
          ),
        );
      }
      return _outcomeFromSessionBody(body);
    } on DioException catch (e) {
      final failure = _errors.fromDio(e);
      return AuthOutcome.failure(reasonCode: _mapFailureToReasonCode(failure));
    }
  }

  @override
  Future<OtpChallenge> requestOtp({required String identifier}) async {
    try {
      final res = await _dio.post<Map<String, Object?>>(
        _otpRequestPath,
        data: {'identifier': identifier, 'channel': 'auto'},
      );
      return _otpFromBody(res.data ?? const {});
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<OtpChallenge> resendOtp({required String challengeId}) async {
    try {
      final res = await _dio.post<Map<String, Object?>>(
        _otpRequestPath,
        data: {'challengeId': challengeId, 'channel': 'auto'},
      );
      return _otpFromBody(res.data ?? const {});
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<AuthOutcome> verifyOtp({
    required String challengeId,
    required String code,
  }) async {
    try {
      // Spec `VerifyOtpRequest` only requires {challengeId, code}; the
      // backend infers the intent (register vs login MFA vs password reset)
      // from the challenge id, so we don't send one.
      final res = await _dio.post<Map<String, Object?>>(
        _otpVerifyPath,
        data: {
          'challengeId': challengeId,
          'code': code,
        },
      );
      return _outcomeFromSessionBody(res.data ?? const {});
    } on DioException catch (e) {
      final failure = _errors.fromDio(e);
      return AuthOutcome.failure(reasonCode: _mapFailureToReasonCode(failure));
    }
  }

  @override
  Future<void> requestPasswordReset({required String email}) async {
    try {
      await _dio.post<void>(
        _passwordResetRequestPath,
        data: {'identifier': email, 'channel': 'auto'},
      );
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<void> confirmPasswordReset({
    required String token,
    required String newPassword,
  }) async {
    try {
      // The customer surface uses `resetChallengeId` + `code`; the legacy
      // interface compresses both into a single `token` string. We accept
      // either "challengeId:code" (preferred) or treat the whole value as
      // a single opaque token for back-compat with stub callers.
      final parts = token.split(':');
      final body = parts.length == 2
          ? {
              'resetChallengeId': parts[0],
              'code': parts[1],
              'newPassword': newPassword,
            }
          : {
              'token': token,
              'newPassword': newPassword,
            };
      await _dio.post<void>(_passwordResetCompletePath, data: body);
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  AuthOutcome _outcomeFromSessionBody(Map<String, Object?> body) {
    // MFA challenges are checked first: a valid MFA login response may
    // omit tokens entirely until the second factor is verified, so a
    // missing-token check before MFA would mis-route into `identity.gap`.
    if (body['mfaRequired'] == true) {
      final mfaChallengeId = body['mfaChallengeId']?.toString();
      if (mfaChallengeId != null && mfaChallengeId.isNotEmpty) {
        return AuthOutcome.requiresOtp(
          otpChallenge: OtpChallenge(
            challengeId: mfaChallengeId,
            channel: body['mfaChannel']?.toString() ?? 'sms',
            retryAfterSeconds: _readInt(body['mfaResendInSeconds']) ?? 30,
          ),
        );
      }
    }
    final accessToken = body['accessToken']?.toString();
    final refreshToken = body['refreshToken']?.toString();
    if (accessToken == null || refreshToken == null) {
      // Some flows (e.g. register-then-otp) return only an OTP challenge.
      final otpChallengeId = body['otpChallengeId']?.toString();
      if (otpChallengeId != null && otpChallengeId.isNotEmpty) {
        return AuthOutcome.requiresOtp(
          otpChallenge: OtpChallenge(
            challengeId: otpChallengeId,
            channel: body['otpChannel']?.toString() ?? 'sms',
            retryAfterSeconds: _readInt(body['otpResendInSeconds']) ?? 30,
          ),
        );
      }
      return const AuthOutcome.failure(reasonCode: 'identity.gap');
    }
    final profile =
        (body['profile'] is Map) ? body['profile'] as Map<Object?, Object?> : const {};
    return AuthOutcome.success(
      accessToken: accessToken,
      refreshToken: refreshToken,
      customerId: body['accountId']?.toString(),
      email: profile['email']?.toString() ?? body['email']?.toString(),
      displayName:
          profile['displayName']?.toString() ?? body['displayName']?.toString(),
      isVerified: profile['emailConfirmed'] == true ||
          profile['phoneConfirmed'] == true,
    );
  }

  OtpChallenge _otpFromBody(Map<String, Object?> body) {
    return OtpChallenge(
      challengeId: body['otpChallengeId']?.toString() ?? '',
      channel: body['channel']?.toString() ??
          body['otpChannel']?.toString() ??
          'sms',
      retryAfterSeconds: _readInt(body['resendAvailableInSeconds']) ??
          _readInt(body['retryAfterSeconds']) ??
          30,
    );
  }

  int? _readInt(Object? raw) {
    if (raw is num) return raw.toInt();
    if (raw is String) return int.tryParse(raw);
    return null;
  }

  /// Translate a typed [Failure] to a stable reason code that the bloc
  /// layer + l10n keys consume. Keep this list short and explicit — every
  /// addition needs a matching l10n entry.
  String _mapFailureToReasonCode(Failure failure) {
    if (failure is AuthFailure) return 'identity.bad_credentials';
    if (failure is ForbiddenFailure) return 'identity.forbidden';
    if (failure is ConflictFailure) return 'identity.conflict';
    if (failure is NotFoundFailure) return 'identity.not_found';
    if (failure is ValidationFailure) {
      if ((failure.retryAfterSeconds ?? 0) > 0) {
        return 'identity.rate_limited';
      }
      return 'identity.validation';
    }
    if (failure is OfflineFailure) return 'network.offline';
    if (failure is NetworkFailure) return 'network.unreachable';
    return 'identity.server_error';
  }
}
