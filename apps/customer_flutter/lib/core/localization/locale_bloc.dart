import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

/// SM-5: LocaleAndDirection. States: EN_LTR, AR_RTL.
enum AppLocale {
  en('en', 'LTR'),
  ar('ar', 'RTL');

  const AppLocale(this.code, this.direction);
  final String code;
  final String direction;

  bool get isRtl => this == ar;
}

@immutable
class LocaleState {
  const LocaleState(this.locale);
  final AppLocale locale;

  @override
  bool operator ==(Object other) =>
      identical(this, other) || (other is LocaleState && other.locale == locale);

  @override
  int get hashCode => locale.hashCode;
}

@immutable
sealed class LocaleEvent {
  const LocaleEvent();
}

class LanguageToggled extends LocaleEvent {
  const LanguageToggled();
}

class LanguageSet extends LocaleEvent {
  const LanguageSet(this.locale);
  final AppLocale locale;
}

class LocaleBloc extends Bloc<LocaleEvent, LocaleState> {
  LocaleBloc({AppLocale? initial})
      : super(LocaleState(initial ?? _resolveInitial())) {
    on<LanguageToggled>((_, emit) {
      emit(LocaleState(state.locale == AppLocale.en ? AppLocale.ar : AppLocale.en));
    });
    on<LanguageSet>((event, emit) => emit(LocaleState(event.locale)));
  }

  static AppLocale _resolveInitial() {
    final dispatcher = PlatformDispatcher.instance;
    final locales = dispatcher.locales;
    for (final l in locales) {
      if (l.languageCode == 'ar') return AppLocale.ar;
      if (l.languageCode == 'en') return AppLocale.en;
    }
    return AppLocale.en;
  }
}
