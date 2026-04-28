import 'package:dio/dio.dart';

typedef LocaleProvider = String Function();
typedef MarketProvider = String Function();

class LocaleMarketInterceptor extends Interceptor {
  LocaleMarketInterceptor({
    required this.locale,
    required this.market,
  });

  final LocaleProvider locale;
  final MarketProvider market;

  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler handler) {
    options.headers['Accept-Language'] = locale();
    options.headers['X-Market-Code'] = market();
    handler.next(options);
  }
}
