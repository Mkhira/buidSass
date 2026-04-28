import 'package:customer_flutter/core/market/market_resolver.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('session ksa wins over account', () {
    final r = MarketResolver(sessionMarket: 'ksa', accountMarket: 'eg');
    expect(r.resolve(), Market.ksa);
  });

  test('session eg wins over account ksa', () {
    final r = MarketResolver(sessionMarket: 'eg', accountMarket: 'ksa');
    expect(r.resolve(), Market.eg);
  });

  test('falls back to account when session is null', () {
    final r = MarketResolver(accountMarket: 'eg');
    expect(r.resolve(), Market.eg);
  });

  test('updateFromSession overrides previous resolution', () {
    final r = MarketResolver(accountMarket: 'eg');
    r.updateFromSession('ksa');
    expect(r.resolve(), Market.ksa);
  });

  test('SA country code maps to ksa', () {
    final r = MarketResolver(sessionMarket: 'SA');
    expect(r.resolve(), Market.ksa);
  });

  test('Market currencies are SAR/EGP', () {
    expect(Market.ksa.currency, 'SAR');
    expect(Market.eg.currency, 'EGP');
  });
}
