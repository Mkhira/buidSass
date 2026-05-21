import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/verification_list_bloc.dart';
import '../data/models/verification_models.dart';
import '../widgets/verification_state_pill.dart';

/// S-7.1 — customer verification list. Active banner on top, history
/// list below. Mirrors the returns list shape so the user experience is
/// consistent across "trust" surfaces in the More hub.
class VerificationListScreen extends StatelessWidget {
  const VerificationListScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.verificationListTitle)),
      body: BlocBuilder<VerificationListBloc, VerificationListState>(
        builder: (context, state) {
          return switch (state) {
            VerificationListLoading() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            VerificationListFailure(:final reason) => ErrorState(
                title: l10n.commonErrorTitle,
                body: reason,
                onRetry: () => context
                    .read<VerificationListBloc>()
                    .add(const VerificationListRefreshed()),
                retryLabel: l10n.commonRetry,
              ),
            VerificationListLoaded() => _Loaded(state: state),
          };
        },
      ),
    );
  }
}

class _Loaded extends StatelessWidget {
  const _Loaded({required this.state});
  final VerificationListLoaded state;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    if (!state.hasAny) {
      return Column(
        children: [
          Expanded(
            child: EmptyState(
              title: l10n.verificationListEmptyTitle,
              body: l10n.verificationListEmptyBody,
              icon: Icons.verified_outlined,
            ),
          ),
          Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: AppButton(
              label: l10n.verificationStartNewCta,
              expand: true,
              onPressed: () => context.push('/verification/new'),
            ),
          ),
        ],
      );
    }
    return RefreshIndicator(
      onRefresh: () async {
        final bloc = context.read<VerificationListBloc>();
        bloc.add(const VerificationListRefreshed());
        await bloc.stream.firstWhere((s) => s is! VerificationListLoading);
      },
      child: ListView(
        padding: const EdgeInsets.all(AppSpacing.md),
        children: [
          if (state.active.hasCase) ...[
            _ActiveBanner(active: state.active),
            const SizedBox(height: AppSpacing.md),
          ],
          ...state.items.map((item) => _Row(item: item)),
          const SizedBox(height: AppSpacing.md),
          AppButton(
            label: l10n.verificationStartNewCta,
            expand: true,
            onPressed: () => context.push('/verification/new'),
          ),
        ],
      ),
    );
  }
}

class _ActiveBanner extends StatelessWidget {
  const _ActiveBanner({required this.active});
  final VerificationActive active;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toString();
    final dateFmt = DateFormat.yMMMd(locale);
    final id = active.id;
    return Card(
      child: InkWell(
        onTap: id == null ? null : () => context.push('/verification/$id'),
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.md),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      l10n.verificationActiveBannerTitle,
                      style: Theme.of(context).textTheme.titleSmall,
                    ),
                  ),
                  VerificationStatePill(state: active.state),
                ],
              ),
              if (active.kind != null) ...[
                const SizedBox(height: AppSpacing.xs),
                Text(
                  _kindLabel(context, active.kind!),
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ],
              if (active.expiresAt != null) ...[
                const SizedBox(height: AppSpacing.xs),
                Text(
                  l10n.verificationActiveExpiresOn(
                    dateFmt.format(active.expiresAt!.toLocal()),
                  ),
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ],
              if (active.state == 'info_requested' && id != null) ...[
                const SizedBox(height: AppSpacing.sm),
                AppButton(
                  label: l10n.verificationResumeCta,
                  onPressed: () => context.push('/verification/$id'),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _Row extends StatelessWidget {
  const _Row({required this.item});
  final VerificationListItem item;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toString();
    final dateFmt = DateFormat.yMMMd(locale);
    return Card(
      child: InkWell(
        onTap: () => context.push('/verification/${item.id}'),
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.md),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      _kindLabel(context, item.kind),
                      style: Theme.of(context).textTheme.titleSmall,
                    ),
                  ),
                  VerificationStatePill(state: item.state),
                ],
              ),
              const SizedBox(height: AppSpacing.xs),
              Text(
                l10n.verificationListSubmittedOn(dateFmt.format(item.createdAt)),
                style: Theme.of(context).textTheme.bodySmall,
              ),
              if (item.expiresAt != null) ...[
                const SizedBox(height: AppSpacing.xs),
                Text(
                  l10n.verificationActiveExpiresOn(
                    dateFmt.format(item.expiresAt!.toLocal()),
                  ),
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

String _kindLabel(BuildContext context, String kind) {
  // V1 only has business_license. Unknown kinds render as the raw value
  // — server-driven copy lives in the schema (S-7.2) where applicable.
  if (kind == 'business_license') {
    return AppLocalizations.of(context).verificationKindBusinessLicense;
  }
  return kind;
}
