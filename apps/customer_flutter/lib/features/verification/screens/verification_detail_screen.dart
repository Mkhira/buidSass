import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/verification_detail_bloc.dart';
import '../data/models/verification_models.dart';
import '../widgets/document_slot_tile.dart';
import '../widgets/verification_state_pill.dart';

/// S-7.3 verification detail. Renders timeline + fields + documents +
/// requested-info checklist. Document upload runs with bounded
/// parallelism through the bloc's semaphore.
class VerificationDetailScreen extends StatelessWidget {
  const VerificationDetailScreen({super.key, required this.verificationId});

  final String verificationId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.verificationDetailTitle)),
      body: BlocBuilder<VerificationDetailBloc, VerificationDetailState>(
        builder: (context, state) {
          return switch (state) {
            VerificationDetailLoading() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            VerificationDetailFailure(:final reason) => ErrorState(
                title: l10n.commonErrorTitle,
                body: reason,
                retryLabel: l10n.commonRetry,
                onRetry: () => context
                    .read<VerificationDetailBloc>()
                    .add(const VerificationDetailRefreshed()),
              ),
            VerificationDetailLoaded() => _Loaded(state: state),
          };
        },
      ),
    );
  }
}

class _Loaded extends StatelessWidget {
  const _Loaded({required this.state});
  final VerificationDetailLoaded state;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final detail = state.detail;

    return DocumentSlotDispatcher(
      onPicked: (slotKey, picked) {
        context.read<VerificationDetailBloc>().add(
              VerificationDocumentUploadRequested(
                slotKey: slotKey,
                bytes: picked.bytes,
                filename: picked.filename,
              ),
            );
      },
      child: RefreshIndicator(
        onRefresh: () async {
          final bloc = context.read<VerificationDetailBloc>();
          bloc.add(const VerificationDetailRefreshed());
          await bloc.stream
              .firstWhere((s) => s is! VerificationDetailLoading);
        },
        child: ListView(
          padding: const EdgeInsets.all(AppSpacing.md),
          children: [
            _HeaderCard(detail: detail),
            const SizedBox(height: AppSpacing.md),
            if (detail.state == 'info_requested' &&
                detail.requestedInfo.isNotEmpty) ...[
              _RequestedInfoCard(detail: detail, uploads: state.uploads),
              const SizedBox(height: AppSpacing.md),
            ],
            Text(
              l10n.verificationDetailDocumentsLabel,
              style: Theme.of(context).textTheme.titleSmall,
            ),
            const SizedBox(height: AppSpacing.sm),
            // Render any slot that either has a server-side document
            // OR has a local upload in progress. Slots that the user
            // hasn't touched yet are rendered as part of the
            // requested-info card above.
            ..._documentSlots(detail, state.uploads).map(
              (slotKey) => Padding(
                padding: const EdgeInsets.only(bottom: AppSpacing.sm),
                child: Builder(
                  builder: (innerContext) {
                    return DocumentSlotTile(
                      slotKey: slotKey,
                      slotLabel: slotKey,
                      uploadState:
                          state.uploads[slotKey] ?? SlotUploadState.idle,
                      alreadyUploaded: detail.documents
                          .any((d) => d.slotKey == slotKey),
                      onPick: pickDocumentImage,
                      onRetry: () async {
                        // Re-pick on retry: the bloc only sees a
                        // fresh upload event with new bytes (we don't
                        // cache image bytes across retries to avoid
                        // memory pressure on Android).
                        final dispatcher =
                            DocumentSlotDispatcher.of(innerContext);
                        final picked = await pickDocumentImage();
                        if (picked != null) {
                          dispatcher.onPicked(slotKey, picked);
                        }
                      },
                    );
                  },
                ),
              ),
            ),
            const SizedBox(height: AppSpacing.md),
            _TimelineCard(detail: detail),
            const SizedBox(height: AppSpacing.md),
            if (detail.state == 'info_requested')
              AppButton(
                label: l10n.verificationDetailResubmitCta,
                expand: true,
                onPressed: state.resubmitReady
                    ? () => context.push('/verification/${detail.id}/resubmit')
                    : null,
              ),
            if (detail.state == 'approved') ...[
              const SizedBox(height: AppSpacing.sm),
              AppButton(
                label: l10n.verificationDetailRenewCta,
                expand: true,
                onPressed: () => context.push(
                  '/verification/renew?prior=${Uri.encodeComponent(detail.id)}',
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }

  Iterable<String> _documentSlots(
    VerificationDetail detail,
    Map<String, SlotUploadState> uploads,
  ) {
    final keys = <String>{
      ...detail.documents.map((d) => d.slotKey),
      ...uploads.keys,
    };
    return keys;
  }
}

class _HeaderCard extends StatelessWidget {
  const _HeaderCard({required this.detail});
  final VerificationDetail detail;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toString();
    final dateFmt = DateFormat.yMMMd(locale);
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    detail.kind,
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                ),
                VerificationStatePill(state: detail.state),
              ],
            ),
            const SizedBox(height: AppSpacing.xs),
            Text(
              l10n.verificationListSubmittedOn(
                dateFmt.format(detail.createdAt),
              ),
              style: Theme.of(context).textTheme.bodySmall,
            ),
            if (detail.fields.isNotEmpty) ...[
              const SizedBox(height: AppSpacing.sm),
              Text(
                l10n.verificationDetailFieldsLabel,
                style: Theme.of(context).textTheme.titleSmall,
              ),
              const SizedBox(height: AppSpacing.xs),
              for (final entry in detail.fields.entries)
                Padding(
                  padding: const EdgeInsets.only(bottom: 4),
                  child: Text(
                    '${entry.key}: ${entry.value}',
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ),
            ],
          ],
        ),
      ),
    );
  }
}

class _RequestedInfoCard extends StatelessWidget {
  const _RequestedInfoCard({required this.detail, required this.uploads});
  final VerificationDetail detail;
  final Map<String, SlotUploadState> uploads;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Card(
      color: AppColors.warning.withValues(alpha: 0.08),
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              l10n.verificationDetailRequestedInfoTitle,
              style: Theme.of(context).textTheme.titleSmall?.copyWith(
                    color: AppColors.warning,
                    fontWeight: FontWeight.w700,
                  ),
            ),
            const SizedBox(height: AppSpacing.sm),
            for (final ri in detail.requestedInfo) _requestedRow(context, ri),
          ],
        ),
      ),
    );
  }

  Widget _requestedRow(BuildContext context, VerificationRequestedInfo ri) {
    final l10n = AppLocalizations.of(context);
    final done = ri.kind == 'doc'
        ? detail.documents.any((d) => d.slotKey == ri.key) ||
            uploads[ri.key]?.status == SlotUploadStatus.ready
        : (detail.fields[ri.key] is String &&
            (detail.fields[ri.key] as String).isNotEmpty);
    final label = ri.kind == 'doc'
        ? l10n.verificationDetailRequestedDoc(ri.key)
        : l10n.verificationDetailRequestedField(ri.key);
    return Padding(
      padding: const EdgeInsets.only(bottom: AppSpacing.xs),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(
            done ? Icons.check_circle : Icons.radio_button_unchecked,
            size: 18,
            color: done ? AppColors.success : AppColors.warning,
          ),
          const SizedBox(width: AppSpacing.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(label, style: Theme.of(context).textTheme.bodyMedium),
                if (ri.note != null && ri.note!.isNotEmpty)
                  Text(
                    ri.note!,
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                if (ri.kind == 'doc' && !done) ...[
                  const SizedBox(height: AppSpacing.xs),
                  DocumentSlotTile(
                    slotKey: ri.key,
                    slotLabel: ri.key,
                    uploadState: uploads[ri.key] ?? SlotUploadState.idle,
                    alreadyUploaded: false,
                    onPick: pickDocumentImage,
                    onRetry: () {},
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _TimelineCard extends StatelessWidget {
  const _TimelineCard({required this.detail});
  final VerificationDetail detail;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toString();
    final dateFmt = DateFormat.yMMMd(locale).add_jm();
    if (detail.timeline.isEmpty) return const SizedBox.shrink();
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              l10n.verificationDetailTimelineLabel,
              style: Theme.of(context).textTheme.titleSmall,
            ),
            const SizedBox(height: AppSpacing.sm),
            for (final ev in detail.timeline)
              Padding(
                padding: const EdgeInsets.only(bottom: AppSpacing.sm),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const Icon(Icons.circle, size: 8),
                    const SizedBox(width: AppSpacing.sm),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            _timelineLabel(l10n, ev.kind),
                            style: Theme.of(context).textTheme.bodyMedium,
                          ),
                          Text(
                            '${_actorLabel(l10n, ev.actor)} · '
                            '${dateFmt.format(ev.occurredAt.toLocal())}',
                            style: Theme.of(context).textTheme.bodySmall,
                          ),
                          if (ev.note != null && ev.note!.isNotEmpty)
                            Text(
                              ev.note!,
                              style: Theme.of(context).textTheme.bodySmall,
                            ),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
          ],
        ),
      ),
    );
  }

  String _timelineLabel(AppLocalizations l10n, String kind) {
    return switch (kind) {
      'submitted' => l10n.verificationTimelineSubmitted,
      'info_requested' => l10n.verificationTimelineInfoRequested,
      'approved' => l10n.verificationTimelineApproved,
      'rejected' => l10n.verificationTimelineRejected,
      'expired' => l10n.verificationTimelineExpired,
      _ => kind,
    };
  }

  String _actorLabel(AppLocalizations l10n, String? actor) {
    return switch (actor) {
      'customer' => l10n.verificationActorCustomer,
      'admin' => l10n.verificationActorAdmin,
      'system' => l10n.verificationActorSystem,
      _ => '',
    };
  }
}
