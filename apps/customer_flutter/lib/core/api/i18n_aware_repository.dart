import 'dart:async';

import '../localization/locale_bloc.dart';

/// Mixin for repositories that hit endpoints listed in
/// `contracts/locale-aware-endpoints.md`. On a [LocaleChanged] event the
/// repository discards any in-flight response and re-issues with the new
/// `Accept-Language`, and any cached response is invalidated.
mixin I18nAwareRepository {
  bool get isI18nBearing => true;

  StreamSubscription<LocaleState>? _sub;
  final StreamController<void> _localeChangesCtrl =
      StreamController<void>.broadcast();

  Stream<void> get localeChanges => _localeChangesCtrl.stream;

  /// Bind the repository to a [LocaleBloc] so subsequent
  /// [LocaleState] emissions trigger [discardInflightOnLocaleChange] and
  /// fan out via [localeChanges].
  void bindLocale(LocaleBloc bloc) {
    _sub?.cancel();
    _sub = bloc.stream.listen((_) {
      discardInflightOnLocaleChange();
      _localeChangesCtrl.add(null);
    });
  }

  /// Concrete repositories override this to cancel in-flight Dio requests
  /// and clear cache.
  void discardInflightOnLocaleChange() {}

  Future<void> disposeI18nAware() async {
    await _sub?.cancel();
    await _localeChangesCtrl.close();
  }
}
