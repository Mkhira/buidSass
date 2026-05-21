import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/awaiting_approval_bloc.dart';
import '../data/models/quote_models.dart';
import '../widgets/quote_state_pill.dart';

/// S-8.2 — approver "awaiting my approval" list.
class AwaitingApprovalScreen extends StatelessWidget {
  const AwaitingApprovalScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.quotesAwaitingTitle)),
      body: BlocBuilder<AwaitingApprovalBloc, AwaitingApprovalState>(
        builder: (context, state) {
          return switch (state) {
            AwaitingApprovalLoading() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            AwaitingApprovalEmpty() => EmptyState(
                title: l10n.quotesAwaitingEmpty,
                body: l10n.quotesAwaitingEmptyBody,
                icon: Icons.task_alt_outlined,
              ),
            AwaitingApprovalLoaded() => RefreshIndicator(
                onRefresh: () async {
                  final bloc = context.read<AwaitingApprovalBloc>();
                  bloc.add(const AwaitingApprovalRefreshed());
                  await bloc.stream
                      .firstWhere((s) => s is! AwaitingApprovalLoading)
                      .timeout(
                        const Duration(seconds: 10),
                        onTimeout: () => bloc.state,
                      );
                },
                child: ListView.builder(
                  padding: const EdgeInsets.all(AppSpacing.md),
                  itemCount: state.items.length,
                  itemBuilder: (context, i) => _Row(item: state.items[i]),
                ),
              ),
            AwaitingApprovalFailure() => ErrorState(
                title: l10n.commonErrorTitle,
                body: l10n.commonErrorBody,
                retryLabel: l10n.commonRetry,
                onRetry: () => context
                    .read<AwaitingApprovalBloc>()
                    .add(const AwaitingApprovalRefreshed()),
              ),
          };
        },
      ),
    );
  }
}

class _Row extends StatelessWidget {
  const _Row({required this.item});
  final QuoteListItem item;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toString();
    final dateFmt = DateFormat.yMMMd(locale);
    return Card(
      child: InkWell(
        onTap: () => context.push('/quotes/${item.id}'),
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.md),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      item.quoteNumber,
                      style: Theme.of(context).textTheme.titleSmall,
                    ),
                  ),
                  QuoteStatePill(state: item.state),
                ],
              ),
              if (item.submittedByName != null) ...[
                const SizedBox(height: AppSpacing.xs),
                Text(
                  l10n.quotesAwaitingSubmittedBy(item.submittedByName!),
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ],
              if (item.submittedAt != null) ...[
                const SizedBox(height: AppSpacing.xs),
                Text(
                  dateFmt.format(item.submittedAt!.toLocal()),
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
