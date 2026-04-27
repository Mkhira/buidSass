import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';

class DioFactoryConfig {
  const DioFactoryConfig({
    required this.baseUrl,
    required this.allowInsecureBackend,
    this.connectTimeout = const Duration(seconds: 15),
    this.receiveTimeout = const Duration(seconds: 30),
    this.sendTimeout = const Duration(seconds: 15),
  });

  factory DioFactoryConfig.fromEnvironment() {
    const baseUrl = String.fromEnvironment('API_BASE_URL', defaultValue: '');
    const allowInsecureFlag =
        String.fromEnvironment('ALLOW_INSECURE_BACKEND', defaultValue: '');
    final allow = allowInsecureFlag == '1' || (kDebugMode && allowInsecureFlag.isEmpty);
    return DioFactoryConfig(baseUrl: baseUrl, allowInsecureBackend: allow);
  }

  final String baseUrl;
  final bool allowInsecureBackend;
  final Duration connectTimeout;
  final Duration receiveTimeout;
  final Duration sendTimeout;
}

class DioFactory {
  const DioFactory(this.config);

  final DioFactoryConfig config;

  Dio create() {
    return Dio(
      BaseOptions(
        baseUrl: config.baseUrl,
        connectTimeout: config.connectTimeout,
        receiveTimeout: config.receiveTimeout,
        sendTimeout: config.sendTimeout,
        headers: const {'Accept': 'application/json'},
      ),
    );
    // HttpsBearerGuardInterceptor is registered last in
    // ApiModule._buildDio so it sees the Authorization header that
    // AuthInterceptor attached and can refuse non-https Bearer attaches.
  }
}

/// FR-015b — refuses to attach `Authorization: Bearer …` to a non-https URL
/// unless `ALLOW_INSECURE_BACKEND=1`. The flag defaults to on for debug builds
/// (auto-set by `tool/dev/run.sh`) and off for release builds.
class HttpsBearerGuardInterceptor extends Interceptor {
  HttpsBearerGuardInterceptor({required this.allowInsecure});

  final bool allowInsecure;

  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler handler) {
    final auth = options.headers['Authorization'] ?? options.headers['authorization'];
    if (auth is String && auth.startsWith('Bearer ')) {
      final scheme = options.uri.scheme.toLowerCase();
      if (scheme != 'https' && !allowInsecure) {
        options.headers.remove('Authorization');
        options.headers.remove('authorization');
        assert(
          false,
          'Refusing to attach Authorization: Bearer over $scheme — '
          'set --dart-define=ALLOW_INSECURE_BACKEND=1 for local debug only.',
        );
      }
    }
    handler.next(options);
  }
}
