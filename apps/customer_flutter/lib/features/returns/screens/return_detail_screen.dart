import 'package:cached_network_image/cached_network_image.dart';
import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/return_detail_bloc.dart';
import '../data/models/return_models.dart';
import '../widgets/return_state_pill.dart';

/// S-6.3 — return detail. Renders the state pill, timeline, items
/// (with photo gallery), refund block, and rejection reason when the
/// state is `rejected`.
class ReturnDetailScreen extends StatelessWidget {
  const ReturnDetailScreen({super.key, required this.returnId});
  final String returnId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.returnDetailTitle)),
      body: BlocBuilder<ReturnDetailBloc, ReturnDetailState>(
        builder: (context, state) {
          return switch (state) {
            ReturnDetailLoading() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            ReturnDetailFailure(:final reason) => ErrorState(
                title: l10n.commonErrorTitle,
                body: reason,
                onRetry: () => context
                    .read<ReturnDetailBloc>()
                    .add(const ReturnDetailRefreshed()),
                retryLabel: l10n.commonRetry,
              ),
            ReturnDetailLoaded(:final detail) => RefreshIndicator(
                onRefresh: () async {
                  final bloc = context.read<ReturnDetailBloc>();
                  bloc.add(const ReturnDetailRefreshed());
                  await bloc.stream.firstWhere((s) => s is! ReturnDetailLoading);
                },
                child: _LoadedBody(detail: detail),
              ),
          };
        },
      ),
    );
  }
}

class _LoadedBody extends StatelessWidget {
  const _LoadedBody({required this.detail});
  final ReturnDetail detail;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return ListView(
      padding: const EdgeInsets.all(AppSpacing.md),
      children: [
        Row(
          children: [
            Expanded(
              child: Text(detail.returnNumber,
                  style: Theme.of(context).textTheme.titleMedium),
            ),
            ReturnStatePill(state: detail.state),
          ],
        ),
        const SizedBox(height: AppSpacing.xs),
        Text(l10n.returnsListOrderLabel(detail.orderNumber),
            style: Theme.of(context).textTheme.bodySmall),
        const Divider(height: AppSpacing.lg),
        // Rejection reason — rendered prominently per S-6.3 AC when
        // the return is `rejected`. We surface it ABOVE the timeline so
        // the user sees the resolution before scrolling through history.
        if (detail.rejection != null && _hasRejectionContent(detail.rejection!))
          _RejectionBlock(rejection: detail.rejection!),
        _SectionTitle(text: l10n.returnDetailTimelineTitle),
        _Timeline(events: detail.timeline),
        const Divider(height: AppSpacing.lg),
        _SectionTitle(text: l10n.returnDetailLinesTitle),
        for (final line in detail.lines) _LineBlock(line: line),
        if (detail.refund != null) ...[
          const Divider(height: AppSpacing.lg),
          _SectionTitle(text: l10n.returnDetailRefundTitle),
          _RefundBlock(refund: detail.refund!),
        ],
      ],
    );
  }

  bool _hasRejectionContent(ReturnRejection r) =>
      (r.reason ?? '').isNotEmpty || (r.noteToCustomer ?? '').isNotEmpty;
}

class _SectionTitle extends StatelessWidget {
  const _SectionTitle({required this.text});
  final String text;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.sm),
      child: Text(text, style: Theme.of(context).textTheme.titleSmall),
    );
  }
}

class _Timeline extends StatelessWidget {
  const _Timeline({required this.events});
  final List<ReturnTimelineEvent> events;

  @override
  Widget build(BuildContext context) {
    if (events.isEmpty) {
      return Padding(
        padding: const EdgeInsets.symmetric(vertical: AppSpacing.sm),
        child: Text(AppLocalizations.of(context).commonEmpty),
      );
    }
    final locale = Localizations.localeOf(context).toString();
    final dateFmt = DateFormat.yMMMd(locale).add_jm();
    return Column(
      children: [
        for (final e in events)
          ListTile(
            dense: true,
            contentPadding: EdgeInsets.zero,
            leading: const Icon(Icons.history),
            title: Text(_label(context, e.kind)),
            subtitle: Text(
              [
                dateFmt.format(e.occurredAt),
                if (e.actor != null) _actorLabel(context, e.actor!),
                if (e.note != null && e.note!.isNotEmpty) e.note!,
              ].join(' · '),
            ),
          ),
      ],
    );
  }

  String _label(BuildContext context, String kind) {
    final l10n = AppLocalizations.of(context);
    return switch (kind) {
      'created' => l10n.returnTimelineKindCreated,
      'approved' => l10n.returnTimelineKindApproved,
      'received' => l10n.returnTimelineKindReceived,
      'inspected' => l10n.returnTimelineKindInspected,
      'issued' => l10n.returnTimelineKindIssued,
      'rejected' => l10n.returnTimelineKindRejected,
      _ => kind,
    };
  }

  String _actorLabel(BuildContext context, String actor) {
    final l10n = AppLocalizations.of(context);
    return switch (actor) {
      'customer' => l10n.returnTimelineActorCustomer,
      'admin' => l10n.returnTimelineActorAdmin,
      'system' => l10n.returnTimelineActorSystem,
      _ => actor,
    };
  }
}

class _LineBlock extends StatelessWidget {
  const _LineBlock({required this.line});
  final ReturnDetailLine line;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.sm),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          ListTile(
            contentPadding: EdgeInsets.zero,
            title: Text(line.name),
            subtitle: Text([
              l10n.returnWizardLineQty(line.qty),
              _reasonLabel(context, line.reason),
              if (line.note != null && line.note!.isNotEmpty) line.note!,
            ].join(' · ')),
          ),
          if (line.photos.isNotEmpty)
            _PhotoGallery(photos: line.photos),
        ],
      ),
    );
  }

  String _reasonLabel(BuildContext context, String code) {
    final l10n = AppLocalizations.of(context);
    return switch (code) {
      'wrong_item' => l10n.returnReasonWrongItem,
      'damaged' => l10n.returnReasonDamaged,
      'not_as_described' => l10n.returnReasonNotAsDescribed,
      'other' => l10n.returnReasonOther,
      _ => code,
    };
  }
}

/// Horizontally-scrolling thumbnail gallery. Tapping a thumbnail opens
/// a full-screen viewer with InteractiveViewer for pinch-to-zoom — the
/// S-6.3 AC ("Photos rendered as a gallery with zoom").
class _PhotoGallery extends StatelessWidget {
  const _PhotoGallery({required this.photos});
  final List<ReturnPhoto> photos;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 96,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        itemCount: photos.length,
        separatorBuilder: (_, __) => const SizedBox(width: AppSpacing.sm),
        itemBuilder: (context, i) {
          final url = photos[i].url;
          return GestureDetector(
            onTap: () => _open(context, url),
            child: ClipRRect(
              borderRadius: BorderRadius.circular(8),
              child: CachedNetworkImage(
                imageUrl: url,
                width: 96,
                height: 96,
                fit: BoxFit.cover,
                errorWidget: (_, __, ___) => Container(
                  color: AppColors.neutral,
                  child: const Icon(Icons.broken_image_outlined),
                ),
              ),
            ),
          );
        },
      ),
    );
  }

  void _open(BuildContext context, String url) {
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => _PhotoViewer(url: url),
      ),
    );
  }
}

class _PhotoViewer extends StatelessWidget {
  const _PhotoViewer({required this.url});
  final String url;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      appBar: AppBar(
        backgroundColor: Colors.transparent,
        foregroundColor: Colors.white,
      ),
      body: Center(
        child: InteractiveViewer(
          maxScale: 5,
          child: CachedNetworkImage(
            imageUrl: url,
            errorWidget: (_, __, ___) =>
                const Icon(Icons.broken_image_outlined, color: Colors.white),
          ),
        ),
      ),
    );
  }
}

class _RefundBlock extends StatelessWidget {
  const _RefundBlock({required this.refund});
  final ReturnRefund refund;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toString();
    final fmt = NumberFormat.currency(
      locale: locale,
      symbol: refund.currency,
      decimalDigits: 2,
    );
    final formatted = fmt.format(double.tryParse(refund.amount) ?? 0);
    final dateFmt = DateFormat.yMMMd(locale);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(formatted,
            style: Theme.of(context).textTheme.titleMedium?.copyWith(
                  color: AppColors.success,
                  fontWeight: FontWeight.w700,
                )),
        if (refund.method != null && refund.method!.isNotEmpty) ...[
          const SizedBox(height: AppSpacing.xs),
          Text('${l10n.returnDetailRefundMethodLabel}: ${refund.method!}'),
        ],
        if (refund.issuedAt != null) ...[
          const SizedBox(height: AppSpacing.xs),
          Text(
              '${l10n.returnDetailRefundIssuedAtLabel}: ${dateFmt.format(refund.issuedAt!)}'),
        ],
      ],
    );
  }
}

class _RejectionBlock extends StatelessWidget {
  const _RejectionBlock({required this.rejection});
  final ReturnRejection rejection;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Container(
      margin: const EdgeInsets.only(bottom: AppSpacing.md),
      padding: const EdgeInsets.all(AppSpacing.md),
      decoration: BoxDecoration(
        color: AppColors.danger.withValues(alpha: 0.08),
        border: Border.all(color: AppColors.danger),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(Icons.cancel_outlined, color: AppColors.danger),
              const SizedBox(width: AppSpacing.sm),
              Text(l10n.returnDetailRejectionTitle,
                  style: Theme.of(context).textTheme.titleSmall?.copyWith(
                      color: AppColors.danger, fontWeight: FontWeight.w700)),
            ],
          ),
          if (rejection.reason != null && rejection.reason!.isNotEmpty) ...[
            const SizedBox(height: AppSpacing.sm),
            Text(rejection.reason!,
                style: Theme.of(context).textTheme.bodyMedium),
          ],
          if (rejection.noteToCustomer != null &&
              rejection.noteToCustomer!.isNotEmpty) ...[
            const SizedBox(height: AppSpacing.xs),
            Text(rejection.noteToCustomer!,
                style: Theme.of(context).textTheme.bodySmall),
          ],
        ],
      ),
    );
  }
}
