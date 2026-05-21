import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/quote_from_product_bloc.dart';

class QuoteFromProductScreen extends StatelessWidget {
  const QuoteFromProductScreen({super.key, required this.productId});
  final String productId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.quoteFromProductTitle)),
      body: BlocConsumer<QuoteFromProductBloc, QuoteFromProductState>(
        listener: (context, state) {
          if (state is QuoteFromProductDone) {
            context.go('/quotes/${state.result.id}');
          }
        },
        builder: (context, state) {
          return switch (state) {
            QuoteFromProductForm() => _Form(state: state, submitting: false),
            QuoteFromProductSubmitting(:final form) =>
              _Form(state: form, submitting: true),
            QuoteFromProductDone() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
          };
        },
      ),
    );
  }
}

class _Form extends StatelessWidget {
  const _Form({required this.state, required this.submitting});
  final QuoteFromProductForm state;
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
              child: Container(
                width: double.infinity,
                padding: const EdgeInsets.all(AppSpacing.md),
                decoration: BoxDecoration(
                  color: AppColors.danger.withValues(alpha: 0.1),
                  border: Border.all(color: AppColors.danger),
                  borderRadius: BorderRadius.circular(8),
                ),
                child: Text(
                  l10n.commonErrorBody,
                  style: const TextStyle(color: AppColors.danger),
                ),
              ),
            ),
          Expanded(
            child: ListView(
              padding: const EdgeInsets.all(AppSpacing.md),
              children: [
                Text(
                  state.productId,
                  style: Theme.of(context).textTheme.titleSmall,
                ),
                const SizedBox(height: AppSpacing.md),
                TextFormField(
                  initialValue: state.qty.toString(),
                  decoration: InputDecoration(
                    labelText: l10n.quoteFromProductQtyLabel,
                  ),
                  keyboardType: TextInputType.number,
                  onChanged: submitting
                      ? null
                      : (raw) {
                          // Dispatch 0 for empty / unparseable input so
                          // bloc state stays in sync with the field;
                          // canSubmit gates on qty > 0 and disables
                          // the button.
                          final parsed = int.tryParse(raw) ?? 0;
                          context
                              .read<QuoteFromProductBloc>()
                              .add(QuoteFromProductQtyChanged(parsed));
                        },
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
                          .read<QuoteFromProductBloc>()
                          .add(QuoteFromProductTermsChanged(v)),
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
                                .read<QuoteFromProductBloc>()
                                .add(QuoteFromProductEtaChanged(picked));
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
                          .read<QuoteFromProductBloc>()
                          .add(QuoteFromProductNoteChanged(v)),
                ),
              ],
            ),
          ),
          Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: AppButton(
              label: l10n.quoteFromProductSubmitCta,
              expand: true,
              isLoading: submitting,
              onPressed: submitting || !state.canSubmit
                  ? null
                  : () => context
                      .read<QuoteFromProductBloc>()
                      .add(const QuoteFromProductSubmitted()),
            ),
          ),
        ],
      ),
    );
  }
}
