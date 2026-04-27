import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../core/auth/auth_session_bloc.dart';
import '../core/localization/locale_bloc.dart';
import '../generated/l10n/app_localizations.dart';
import 'di.dart';
import 'router.dart';
import 'theme.dart';

class CustomerApp extends StatefulWidget {
  const CustomerApp({super.key});

  @override
  State<CustomerApp> createState() => _CustomerAppState();
}

class _CustomerAppState extends State<CustomerApp> {
  late final _router = buildRouter(sl<AuthSessionBloc>());

  @override
  Widget build(BuildContext context) {
    return MultiBlocProvider(
      providers: [
        BlocProvider<AuthSessionBloc>.value(value: sl<AuthSessionBloc>()),
        BlocProvider<LocaleBloc>.value(value: sl<LocaleBloc>()),
      ],
      child: BlocBuilder<LocaleBloc, LocaleState>(
        builder: (context, localeState) {
          final locale = Locale(localeState.locale.code);
          return MaterialApp.router(
            onGenerateTitle: (ctx) => AppLocalizations.of(ctx).appName,
            theme: CustomerAppTheme.light(),
            darkTheme: CustomerAppTheme.dark(),
            locale: locale,
            supportedLocales: const [Locale('en'), Locale('ar')],
            localizationsDelegates: AppLocalizations.localizationsDelegates,
            routerConfig: _router,
            builder: (ctx, child) => Directionality(
              textDirection: localeState.locale.isRtl
                  ? TextDirection.rtl
                  : TextDirection.ltr,
              child: child ?? const SizedBox.shrink(),
            ),
          );
        },
      ),
    );
  }
}
