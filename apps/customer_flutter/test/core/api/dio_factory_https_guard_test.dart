import 'package:customer_flutter/core/api/dio_factory.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('HttpsBearerGuardInterceptor', () {
    test('strips Bearer header on http when allowInsecure=false', () {
      final guard = HttpsBearerGuardInterceptor(allowInsecure: false);
      final options = RequestOptions(path: 'http://example.com/v1/me')
        ..headers['Authorization'] = 'Bearer secret';
      final handler = _RecordingHandler();

      try {
        guard.onRequest(options, handler);
      } on AssertionError {
        // assertion fires in debug; production strips silently.
      }

      expect(options.headers['Authorization'], isNull);
    });

    test('keeps Bearer header on http when allowInsecure=true', () {
      final guard = HttpsBearerGuardInterceptor(allowInsecure: true);
      final options = RequestOptions(path: 'http://example.com/v1/me')
        ..headers['Authorization'] = 'Bearer secret';
      final handler = _RecordingHandler();

      guard.onRequest(options, handler);

      expect(options.headers['Authorization'], 'Bearer secret');
    });

    test('keeps Bearer on https unconditionally', () {
      final guard = HttpsBearerGuardInterceptor(allowInsecure: false);
      final options = RequestOptions(path: 'https://example.com/v1/me')
        ..headers['Authorization'] = 'Bearer secret';
      final handler = _RecordingHandler();

      guard.onRequest(options, handler);

      expect(options.headers['Authorization'], 'Bearer secret');
    });
  });
}

class _RecordingHandler extends RequestInterceptorHandler {
  _RecordingHandler();
}
