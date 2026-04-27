import 'dart:async';

import 'package:dio/dio.dart';

import '../auth/secure_token_store.dart';

typedef RefreshTokenFn = Future<RefreshOutcome> Function(String refreshToken);

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
/// original request **once**. If refresh fails the request error propagates
/// and the AuthSessionBloc transitions to `RefreshFailed`.
class AuthInterceptor extends QueuedInterceptor {
  AuthInterceptor({
    required this.tokenStore,
    required this.refresh,
    required this.dio,
  });

  final SecureTokenStore tokenStore;
  final RefreshTokenFn refresh;
  final Dio dio;

  bool _refreshing = false;

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
    if (_refreshing) {
      return handler.next(err);
    }
    _refreshing = true;
    try {
      final outcome = await refresh(refreshToken);
      if (!outcome.ok) {
        await tokenStore.clear();
        return handler.next(err);
      }
      await tokenStore.writeTokens(
        accessToken: outcome.accessToken!,
        refreshToken: outcome.refreshToken!,
      );
      final retryOptions = err.requestOptions
        ..headers['Authorization'] = 'Bearer ${outcome.accessToken}'
        ..extra['retriedAfterRefresh'] = true;
      final response = await dio.fetch<dynamic>(retryOptions);
      handler.resolve(response);
    } on Object {
      handler.next(err);
    } finally {
      _refreshing = false;
    }
  }
}
