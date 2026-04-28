import 'package:dio/dio.dart';

/// IdempotencyInterceptor — checkout submit only. Reads the active
/// `Idempotency-Key` from `RequestOptions.extra['idempotencyKey']`
/// (set by CheckoutBloc) and forwards as a header. Other endpoints
/// pass through unchanged.
class IdempotencyInterceptor extends Interceptor {
  const IdempotencyInterceptor();

  static const String headerName = 'Idempotency-Key';
  static const String extraKey = 'idempotencyKey';

  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler handler) {
    final key = options.extra[extraKey];
    if (key is String && key.isNotEmpty) {
      options.headers[headerName] = key;
    }
    handler.next(options);
  }
}
