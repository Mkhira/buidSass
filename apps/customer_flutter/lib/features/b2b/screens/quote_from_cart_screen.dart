import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/quote_from_cart_bloc.dart';

class QuoteFromCartScreen extends StatelessWidget {
  const QuoteFromCartScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.quoteFromCartTitle)),
      body: BlocConsumer<QuoteFromCartBloc, QuoteFromCartState>(
        listener: (context, state) {
          if (state is QuoteFromCartDone) {
            context.go('/quotes/${state.result.id}');
          }
        },
        builder: (context, state) {
          return switch (state) {
            QuoteFromCartEmpty() => EmptyState(
                title: l10n.quoteFromCartEmptyCart,
                body: l10n.quoteFromCartEmptyCart,
                icon: Icons.shopping_cart_outlined,
              ),
            QuoteFromCartForm() => _Form(state: state, submitting: false),
            QuoteFromCartSubmitting(:final form) =>
              _Form(state: form, submitting: true),
            QuoteFromCartDone() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
          };
        },
      ),
    );
  }
}

class _Form extends StatelessWidget {
  const _Form({required this.state, required this.submitting});
  final QuoteFromCartForm state;
  final bool submitting;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toString();
    final dateFmt = DateFormat.yMMMd(locale);
    return SafeArea(
      child: Column(
        children: [
          if (state.formError != null)
            Padding(
              padding: const EdgeInsets.all(AppSpacing.md),
              child: _ErrorBanner(message: l10n.commonErrorBody),
            ),
          Expanded(
            child: ListView(
              padding: const EdgeInsets.all(AppSpacing.md),
              children: [
                Text(
                  l10n.quoteFromCartHeader,
                  style: Theme.of(context).textTheme.titleSmall,
                ),
                const SizedBox(height: AppSpacing.sm),
                for (final l in state.cartLines)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 4),
                    child: Text(
                      '${l.qty} × ${l.productId}',
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  ),
                const SizedBox(height: AppSpacing.md),
                TextFormField(
                  initialValue: state.terms,
                  decoration: InputDecoration(
                    labelText: l10n.quoteFromCartTermsLabel,
                  ),
                  onChanged: submitting
                      ? null
                      : (v) => context
                          .read<QuoteFromCartBloc>()
                          .add(QuoteFromCartTermsChanged(v)),
                ),
                const SizedBox(height: AppSpacing.md),
                InkWell(
                  onTap: submitting
                      ? null
                      : () async {
                          final now = DateTime.now();
                          final picked = await showDatePicker(
                            context: context,
                            firstDate: now,
                            lastDate: now.add(const Duration(days: 365)),
                            initialDate: state.eta ?? now,
                          );
                          if (picked != null && context.mounted) {
                            context
                                .read<QuoteFromCartBloc>()
                                .add(QuoteFromCartEtaChanged(picked));
                          }
                        },
                  child: InputDecorator(
                    decoration: InputDecoration(
                      labelText: l10n.quoteFromCartEtaLabel,
                    ),
                    child: Text(
                      state.eta == null
                          ? ''
                          : dateFmt.format(state.eta!.toLocal()),
                      style: Theme.of(context).textTheme.bodyMedium,
                    ),
                  ),
                ),
                const SizedBox(height: AppSpacing.md),
                TextFormField(
                  initialValue: state.note,
                  decoration: InputDecoration(
                    labelText: l10n.quoteFromCartNoteLabel,
                  ),
                  maxLines: 3,
                  onChanged: submitting
                      ? null
                      : (v) => context
                          .read<QuoteFromCartBloc>()
                          .add(QuoteFromCartNoteChanged(v)),
                ),
              ],
            ),
          ),
          Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: AppButton(
              label: l10n.quoteFromCartSubmitCta,
              expand: true,
              isLoading: submitting,
              onPressed: submitting || !state.canSubmit
                  ? null
                  : () => context
                      .read<QuoteFromCartBloc>()
                      .add(const QuoteFromCartSubmitted()),
            ),
          ),
        ],
      ),
    );
  }
}

class _ErrorBanner extends StatelessWidget {
  const _ErrorBanner({required this.message});
  final String message;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(AppSpacing.md),
      decoration: BoxDecoration(
        color: AppColors.danger.withValues(alpha: 0.1),
        border: Border.all(color: AppColors.danger),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Text(message, style: const TextStyle(color: AppColors.danger)),
    );
  }
}
