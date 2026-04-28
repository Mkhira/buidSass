import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../core/auth/auth_session_bloc.dart';
import '../../../core/config/feature_flags.dart';
import '../../../core/localization/locale_bloc.dart';
import '../../../generated/l10n/app_localizations.dart';

class MoreScreen extends StatelessWidget {
  const MoreScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final flags = context.read<FeatureFlags>();
    return Scaffold(
      appBar: AppBar(title: Text(l10n.navMore)),
      body: ListView(
        children: [
          AppListTile(
            title: l10n.commonContinue,
            onTap: () => context.go('/more/addresses'),
          ),
          AppListTile(
            title: l10n.moreLanguage,
            trailing: TextButton(
              onPressed: () =>
                  context.read<LocaleBloc>().add(const LanguageToggled()),
              child: Text(
                context.watch<LocaleBloc>().state.locale.code.toUpperCase(),
              ),
            ),
          ),
          AppListTile(
            title: l10n.moreVerification,
            onTap: () => context.go('/more/verification'),
            subtitle: flags.verificationCtaShipped ? null : 'soon',
          ),
          AppListTile(
            title: l10n.moreLogout,
            leading: const Icon(Icons.logout, color: AppColors.danger),
            onTap: () {
              context.read<AuthSessionBloc>()
                ..add(const LogoutRequested())
                ..add(const LogoutCompleted());
              context.go('/');
            },
          ),
        ],
      ),
    );
  }
}
