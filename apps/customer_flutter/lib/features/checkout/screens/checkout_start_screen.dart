import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/checkout_start_bloc.dart';
import '../widgets/conflict_dialog.dart';

class CheckoutStartScreen extends StatelessWidget {
  const CheckoutStartScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return BlocConsumer<CheckoutStartBloc, CheckoutStartState>(
      listener: (context, state) async {
        if (state is CheckoutStartedState) {
          context.go('/checkout/${state.sessionId}/summary');
          return;
        }
        if (state is CheckoutStartConflict) {
          await showConflictDialog(context, state.conflict);
          // The start screen cannot re-issue without a fresh cart
          // snapshot — bounce the user back to /cart so they can
          // resolve the drift there.
          if (context.mounted) context.go('/cart');
        }
      },
      builder: (context, state) {
        return AppScaffold(
          appBar: AppBar(title: Text(l10n.checkoutStartingTitle)),
          body: switch (state) {
            CheckoutStartFailure(:final reason) => ErrorState(
                title: l10n.commonErrorTitle,
                body: reason,
                onRetry: () => Navigator.of(context).pop(),
                retryLabel: l10n.commonRetry,
              ),
            _ => LoadingState(semanticsLabel: l10n.commonLoading),
          },
        );
      },
    );
  }
}
