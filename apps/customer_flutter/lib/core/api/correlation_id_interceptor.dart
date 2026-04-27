import 'dart:math';

import 'package:dio/dio.dart';

class CorrelationIdInterceptor extends Interceptor {
  CorrelationIdInterceptor({String Function()? idGenerator})
      : _idGenerator = idGenerator ?? _defaultUuidV4;

  static const String headerName = 'X-Correlation-Id';

  final String Function() _idGenerator;

  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler handler) {
    options.headers.putIfAbsent(headerName, _idGenerator);
    handler.next(options);
  }
}

String _defaultUuidV4() {
  final random = Random.secure();
  final bytes = List<int>.generate(16, (_) => random.nextInt(256));
  bytes[6] = (bytes[6] & 0x0F) | 0x40; // version 4
  bytes[8] = (bytes[8] & 0x3F) | 0x80; // RFC 4122 variant
  String hex(int from, int to) =>
      bytes.sublist(from, to).map((b) => b.toRadixString(16).padLeft(2, '0')).join();
  return '${hex(0, 4)}-${hex(4, 6)}-${hex(6, 8)}-${hex(8, 10)}-${hex(10, 16)}';
}
