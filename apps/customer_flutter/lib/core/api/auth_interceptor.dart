import 'dart:async';

import 'package:dio/dio.dart';

import '../auth/secure_token_store.dart';

typedef RefreshTokenFn = Future<RefreshOutcome> Function(String refreshToken);
typedef RefreshLifecycleHook = void Function();
typedef RefreshSuccessHook = void Function(
    String accessToken, String refreshToken);

class RefreshOutcome {
  const RefreshOutcome.success({
    required this.accessToken,
    required this.refreshToken,
  }) : ok = true;

  const RefreshOutcome.failure()
      : ok = false,
        accessToken = null,
        refreshToken = null;

  final bool ok;
  final String? accessToken;
  final String? refreshToken;
}

/// AuthInterceptor — attaches `Authorization: Bearer <access>` from secure
/// storage; on 401 with a valid refresh token, refreshes and retries the
/// original request **once**. Concurrent 401s share the same in-flight
/// refresh via a Completer so we never double-refresh.
class AuthInterceptor extends QueuedInterceptor {
  AuthInterceptor({
    required this.tokenStore,
    required this.refresh,
    required this.dio,
    this.onRefreshStarted,
    this.onRefreshSucceeded,
    this.onRefreshFailed,
  });

  final SecureTokenStore tokenStore;
  final RefreshTokenFn refresh;
  final Dio dio;

  /// Optional lifecycle hooks — DI wires these to AuthSessionBloc events
  /// so SM-1 stays in sync with the HTTP layer.
  final RefreshLifecycleHook? onRefreshStarted;
  final RefreshSuccessHook? onRefreshSucceeded;
  final RefreshLifecycleHook? onRefreshFailed;

  /// In-flight refresh — concurrent 401s await the same future so we
  /// only call the spec 004 refresh endpoint once.
  Completer<RefreshOutcome>? _inflight;

  @override
  Future<void> onRequest(
    RequestOptions options,
    RequestInterceptorHandler handler,
  ) async {
    if (options.extra['skipAuth'] == true) {
      return handler.next(options);
    }
    final access = await tokenStore.readAccessToken();
    if (access != null && access.isNotEmpty) {
      options.headers['Authorization'] = 'Bearer $access';
    }
    handler.next(options);
  }

  @override
  Future<void> onError(
    DioException err,
    ErrorInterceptorHandler handler,
  ) async {
    final status = err.response?.statusCode;
    final isRetry = err.requestOptions.extra['retriedAfterRefresh'] == true;
    if (status != 401 || isRetry) {
      return handler.next(err);
    }
    final refreshToken = await tokenStore.readRefreshToken();
    if (refreshToken == null || refreshToken.isEmpty) {
      return handler.next(err);
    }
    try {
      final outcome = await _refreshOnceShared(refreshToken);
      if (!outcome.ok) {
        return handler.next(err);
      }
      // Retry the original request with the new access token.
      final retryOptions = err.requestOptions
        ..headers['Authorization'] = 'Bearer ${outcome.accessToken}'
        ..extra['retriedAfterRefresh'] = true;
      final response = await dio.fetch<dynamic>(retryOptions);
      handler.resolve(response);
    } on Object {
      handler.next(err);
    }
  }

  /// Shared-completer dedup: the first caller fires the actual refresh;
  /// concurrent callers await the same outcome.
  Future<RefreshOutcome> _refreshOnceShared(String refreshToken) async {
    final inflight = _inflight;
    if (inflight != null) return inflight.future;

    final completer = Completer<RefreshOutcome>();
    _inflight = completer;
    onRefreshStarted?.call();
    try {
      final outcome = await refresh(refreshToken);
      if (outcome.ok) {
        await tokenStore.writeTokens(
          accessToken: outcome.accessToken!,
          refreshToken: outcome.refreshToken!,
        );
        onRefreshSucceeded?.call(outcome.accessToken!, outcome.refreshToken!);
      } else {
        await tokenStore.clear();
        onRefreshFailed?.call();
      }
      completer.complete(outcome);
      return outcome;
    } on Object catch (e, st) {
      await tokenStore.clear();
      onRefreshFailed?.call();
      completer.completeError(e, st);
      rethrow;
    } finally {
      _inflight = null;
    }
  }
}
