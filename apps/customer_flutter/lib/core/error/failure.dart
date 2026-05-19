import 'package:flutter/foundation.dart';

/// Sealed [Failure] hierarchy — the only error type a Bloc or repository
/// surfaces upward. See `specs/mobile/phase-1-auth-identity/data-model.md`
/// for the status-code → variant mapping.
///
/// Every variant carries a stable [code] (machine-readable, drives UI
/// branching + telemetry), a localized-ready [message] (display text from
/// the server), and a [correlationId] (last 8 chars surfaced in errors per
/// Principle 25). [details] holds the raw `error.details` payload for
/// validation arrays / cooldown timers / etc.
@immutable
sealed class Failure {
  const Failure({
    required this.code,
    required this.message,
    required this.correlationId,
    this.details,
  });

  final String code;
  final String message;
  final String correlationId;
  final Map<String, Object?>? details;

  /// Short suffix for inline display ("…ab12cd34") without leaking the full
  /// UUID. Returns empty string if [correlationId] is shorter than 8 chars.
  String get correlationIdShort {
    if (correlationId.length < 8) return correlationId;
    return correlationId.substring(correlationId.length - 8);
  }

  @override
  String toString() =>
      '$runtimeType(code=$code, correlationId=$correlationIdShort)';
}

/// 422 — field-level validation errors. [fields] holds per-path messages
/// pulled from `error.details.fields`.
class ValidationFailure extends Failure {
  const ValidationFailure({
    required super.code,
    required super.message,
    required super.correlationId,
    this.fields = const [],
    this.retryAfterSeconds,
    super.details,
  });

  /// `[{path, message, code}, ...]` from `error.details.fields`.
  final List<ValidationFieldError> fields;

  /// Present when the server returned a cooldown (429) folded into the
  /// validation envelope, or when an OTP/reset-token expired (410). UI
  /// uses this to render a countdown.
  final int? retryAfterSeconds;
}

@immutable
class ValidationFieldError {
  const ValidationFieldError({
    required this.path,
    required this.message,
    this.code,
  });
  final String path;
  final String message;
  final String? code;
}

/// 401 — invalid/expired credentials or token. The auth interceptor uses
/// this to decide whether to attempt a single refresh.
class AuthFailure extends Failure {
  const AuthFailure({
    required super.code,
    required super.message,
    required super.correlationId,
    super.details,
  });
}

/// 403 — surfaces as a "not eligible" panel, never as a re-auth prompt.
class ForbiddenFailure extends Failure {
  const ForbiddenFailure({
    required super.code,
    required super.message,
    required super.correlationId,
    super.details,
  });
}

/// 404.
class NotFoundFailure extends Failure {
  const NotFoundFailure({
    required super.code,
    required super.message,
    required super.correlationId,
    super.details,
  });
}

/// 409 — drift / conflict. Phase 4 checkout flow renders the reusable
/// `ConflictDialog` with the delta payload.
class ConflictFailure extends Failure {
  const ConflictFailure({
    required super.code,
    required super.message,
    required super.correlationId,
    super.details,
  });
}

/// 5xx — retry banner with correlation id.
class ServerFailure extends Failure {
  const ServerFailure({
    required super.code,
    required super.message,
    required super.correlationId,
    super.details,
  });
}

/// Network / timeout / cancel — the request did not reach the server (or
/// the response was not received). Distinct from [OfflineFailure] which
/// is the no-radio case.
class NetworkFailure extends Failure {
  const NetworkFailure({
    required super.code,
    required super.message,
    required super.correlationId,
    super.details,
  });
}

/// No network radio / DNS unreachable. UI may render cached views.
class OfflineFailure extends Failure {
  const OfflineFailure({
    required super.code,
    required super.message,
    required super.correlationId,
    super.details,
  });
}
