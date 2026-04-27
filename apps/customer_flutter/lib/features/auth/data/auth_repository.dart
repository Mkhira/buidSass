/// Identity client interface — owned by spec 004. Implementations are
/// generated against `services/backend_api/openapi.identity.json`. The shell
/// ships a stub adapter that surfaces a clear `IdentityGapException` until
/// the generated client lands; UI flows still render their wired states.
abstract class AuthRepository {
  Future<AuthOutcome> login({required String email, required String password});

  Future<AuthOutcome> register({
    required String email,
    required String password,
    required String displayName,
  });

  Future<OtpChallenge> requestOtp({required String identifier});

  Future<OtpChallenge> resendOtp({required String challengeId});

  Future<AuthOutcome> verifyOtp({
    required String challengeId,
    required String code,
  });

  Future<void> requestPasswordReset({required String email});

  Future<void> confirmPasswordReset({
    required String token,
    required String newPassword,
  });
}

class AuthOutcome {
  const AuthOutcome.success({
    required this.accessToken,
    required this.refreshToken,
    required this.customerId,
    required this.email,
    required this.displayName,
    required this.isVerified,
  })  : reasonCode = null,
        ok = true,
        otpChallenge = null;

  const AuthOutcome.requiresOtp({required this.otpChallenge})
      : ok = false,
        accessToken = null,
        refreshToken = null,
        customerId = null,
        email = null,
        displayName = null,
        isVerified = false,
        reasonCode = null;

  const AuthOutcome.failure({required this.reasonCode})
      : ok = false,
        accessToken = null,
        refreshToken = null,
        customerId = null,
        email = null,
        displayName = null,
        isVerified = false,
        otpChallenge = null;

  final bool ok;
  final String? accessToken;
  final String? refreshToken;
  final String? customerId;
  final String? email;
  final String? displayName;
  final bool isVerified;
  final String? reasonCode;
  final OtpChallenge? otpChallenge;
}

class OtpChallenge {
  const OtpChallenge({
    required this.challengeId,
    required this.channel,
    required this.retryAfterSeconds,
  });
  final String challengeId;
  final String channel; // sms | email
  final int retryAfterSeconds;
}

class StubAuthRepository implements AuthRepository {
  @override
  Future<AuthOutcome> login({required String email, required String password}) async {
    throw const IdentityGapException();
  }

  @override
  Future<AuthOutcome> register({
    required String email,
    required String password,
    required String displayName,
  }) async {
    throw const IdentityGapException();
  }

  @override
  Future<OtpChallenge> requestOtp({required String identifier}) async {
    throw const IdentityGapException();
  }

  @override
  Future<OtpChallenge> resendOtp({required String challengeId}) async {
    throw const IdentityGapException();
  }

  @override
  Future<AuthOutcome> verifyOtp({
    required String challengeId,
    required String code,
  }) async {
    throw const IdentityGapException();
  }

  @override
  Future<void> requestPasswordReset({required String email}) async {
    throw const IdentityGapException();
  }

  @override
  Future<void> confirmPasswordReset({
    required String token,
    required String newPassword,
  }) async {
    throw const IdentityGapException();
  }
}

class IdentityGapException implements Exception {
  const IdentityGapException();
  @override
  String toString() => 'Identity client gap — escalate to spec 004 (FR-031).';
}
