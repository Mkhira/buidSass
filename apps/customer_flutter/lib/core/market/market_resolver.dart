import 'dart:ui' show PlatformDispatcher;

/// MarketResolver — derives the active market from session → account →
/// device locale. `*-SA` → ksa, `*-EG` → eg, otherwise ksa (per ADR-010
/// + the spec 014 Assumptions).
enum Market {
  ksa('ksa', 'SAR'),
  eg('eg', 'EGP');

  const Market(this.code, this.currency);
  final String code;
  final String currency;
}

class MarketResolver {
  MarketResolver({String? sessionMarket, String? accountMarket})
      : _sessionMarket = sessionMarket,
        _accountMarket = accountMarket;

  String? _sessionMarket;
  String? _accountMarket;

  void updateFromSession(String? marketCode) {
    _sessionMarket = marketCode;
  }

  void updateFromAccount(String? marketCode) {
    _accountMarket = marketCode;
  }

  Market resolve() {
    final candidates = [_sessionMarket, _accountMarket];
    for (final c in candidates) {
      if (c == null) continue;
      switch (c.toLowerCase()) {
        case 'ksa':
        case 'sa':
          return Market.ksa;
        case 'eg':
          return Market.eg;
      }
    }
    return _resolveFromDeviceLocale();
  }

  static Market _resolveFromDeviceLocale() {
    final locales = PlatformDispatcher.instance.locales;
    for (final l in locales) {
      final country = l.countryCode?.toUpperCase();
      if (country == 'SA') return Market.ksa;
      if (country == 'EG') return Market.eg;
    }
    return Market.ksa;
  }
}
