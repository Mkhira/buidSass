import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/core/localization/locale_bloc.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  blocTest<LocaleBloc, LocaleState>(
    'EN_LTR -> AR_RTL on toggle',
    build: () => LocaleBloc(initial: AppLocale.en),
    act: (b) => b.add(const LanguageToggled()),
    expect: () => const [LocaleState(AppLocale.ar)],
  );

  blocTest<LocaleBloc, LocaleState>(
    'AR_RTL -> EN_LTR on toggle',
    build: () => LocaleBloc(initial: AppLocale.ar),
    act: (b) => b.add(const LanguageToggled()),
    expect: () => const [LocaleState(AppLocale.en)],
  );

  blocTest<LocaleBloc, LocaleState>(
    'LanguageSet emits the requested locale',
    build: () => LocaleBloc(initial: AppLocale.en),
    act: (b) => b.add(const LanguageSet(AppLocale.ar)),
    expect: () => const [LocaleState(AppLocale.ar)],
  );

  test('ar.isRtl is true; en.isRtl is false', () {
    expect(AppLocale.ar.isRtl, isTrue);
    expect(AppLocale.en.isRtl, isFalse);
  });
}
