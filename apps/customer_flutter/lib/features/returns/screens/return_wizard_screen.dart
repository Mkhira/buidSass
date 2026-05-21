import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:image_picker/image_picker.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../../orders/data/models/order_models.dart';
import '../bloc/return_wizard_bloc.dart';
import '../data/models/return_models.dart';
import '../widgets/photo_upload_tile.dart';

/// S-6.2 — return wizard. Sequential form: line selection → reason
/// per selected line → photos (optional) → refund method (optional) →
/// submit. The bloc owns the two distinct idempotency-key classes
/// (one per photo tile, one per wizard intent — spec.md §S-6.2).
class ReturnWizardScreen extends StatelessWidget {
  const ReturnWizardScreen({super.key, required this.orderId});
  final String orderId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return BlocListener<ReturnWizardBloc, ReturnWizardState>(
      listenWhen: (a, b) => b is ReturnWizardSuccess,
      listener: (context, state) {
        if (state is ReturnWizardSuccess) {
          // Routes to the new return's detail screen on success. We
          // replace the wizard so the back button takes the user to
          // order detail, not back into the (now-stale) wizard form.
          context.go('/returns/${state.returnId}');
        }
      },
      child: AppScaffold(
        appBar: AppBar(title: Text(l10n.returnWizardTitle)),
        body: BlocBuilder<ReturnWizardBloc, ReturnWizardState>(
          builder: (context, state) {
            return switch (state) {
              ReturnWizardLoading() =>
                LoadingState(semanticsLabel: l10n.commonLoading),
              ReturnWizardIneligible() => EmptyState(
                  title: l10n.returnWizardIneligibleTitle,
                  body: l10n.returnWizardIneligibleBody,
                  icon: Icons.do_not_disturb_alt_outlined,
                ),
              ReturnWizardForm() => _FormBody(form: state),
              ReturnWizardSubmitting(:final form) => Stack(
                  children: [
                    AbsorbPointer(child: _FormBody(form: form)),
                    Container(
                      color: Colors.black.withValues(alpha: 0.25),
                      child: Center(
                        child: Semantics(
                          label: l10n.returnWizardSubmitting,
                          child: const CircularProgressIndicator(),
                        ),
                      ),
                    ),
                  ],
                ),
              ReturnWizardSuccess() =>
                LoadingState(semanticsLabel: l10n.commonLoading),
              ReturnWizardFailure(:final reason, :final form) => ErrorState(
                  title: l10n.commonErrorTitle,
                  body: form.eligibilityStale
                      ? l10n.returnWizardEligibilityStale
                      : reason,
                  onRetry: () => form.eligibilityStale
                      ? context
                          .read<ReturnWizardBloc>()
                          .add(const ReturnWizardEligibilityRefreshed())
                      : context
                          .read<ReturnWizardBloc>()
                          .add(const ReturnWizardSubmitted()),
                  retryLabel: form.eligibilityStale
                      ? l10n.returnWizardRefreshEligibility
                      : l10n.commonRetry,
                ),
            };
          },
        ),
      ),
    );
  }
}

class _FormBody extends StatelessWidget {
  const _FormBody({required this.form});
  final ReturnWizardForm form;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return ListView(
      padding: const EdgeInsets.all(AppSpacing.md),
      children: [
        if (form.eligibilityStale) _StaleEligibilityBanner(),
        _SectionTitle(text: l10n.returnWizardStepLines),
        for (final eligibilityLine in form.eligibility.lines)
          _LinePicker(
            eligibilityLine: eligibilityLine,
            draft: _findDraft(form.lines, eligibilityLine.productId),
          ),
        const Divider(height: AppSpacing.lg),
        _SectionTitle(text: l10n.returnWizardStepPhotos),
        Text(l10n.returnWizardPhotosCaption,
            style: Theme.of(context).textTheme.bodySmall),
        const SizedBox(height: AppSpacing.sm),
        _PhotoStrip(photos: form.photos),
        const Divider(height: AppSpacing.lg),
        _RefundMethodPicker(active: form.refundMethod),
        if (form.validationError != null)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: AppSpacing.sm),
            child: Text(
              _validationMessage(context, form.validationError!),
              style: const TextStyle(color: AppColors.danger),
            ),
          ),
        const SizedBox(height: AppSpacing.lg),
        FilledButton(
          onPressed: form.canAttemptSubmit
              ? () => context
                  .read<ReturnWizardBloc>()
                  .add(const ReturnWizardSubmitted())
              : null,
          child: Text(l10n.returnWizardSubmit),
        ),
      ],
    );
  }

  ReturnWizardLineDraft? _findDraft(
    List<ReturnWizardLineDraft> drafts,
    String productId,
  ) {
    for (final d in drafts) {
      if (d.productId == productId) return d;
    }
    return null;
  }

  String _validationMessage(BuildContext context, String code) {
    final l10n = AppLocalizations.of(context);
    return switch (code) {
      'select_line' => l10n.returnWizardValidationSelectLine,
      'reason_required' => l10n.returnWizardValidationReasonRequired,
      _ => l10n.commonErrorBody,
    };
  }
}

class _StaleEligibilityBanner extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Container(
      margin: const EdgeInsets.only(bottom: AppSpacing.md),
      padding: const EdgeInsets.all(AppSpacing.md),
      decoration: BoxDecoration(
        color: AppColors.warning.withValues(alpha: 0.12),
        border: Border.all(color: AppColors.warning),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Row(
        children: [
          const Icon(Icons.warning_amber_rounded, color: AppColors.warning),
          const SizedBox(width: AppSpacing.sm),
          Expanded(child: Text(l10n.returnWizardEligibilityStale)),
          TextButton(
            onPressed: () => context
                .read<ReturnWizardBloc>()
                .add(const ReturnWizardEligibilityRefreshed()),
            child: Text(l10n.returnWizardRefreshEligibility),
          ),
        ],
      ),
    );
  }
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

class _LinePicker extends StatelessWidget {
  const _LinePicker({required this.eligibilityLine, required this.draft});
  final ReturnEligibilityLine eligibilityLine;
  final ReturnWizardLineDraft? draft;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    if (!eligibilityLine.eligible) {
      return Opacity(
        opacity: 0.5,
        child: ListTile(
          contentPadding: EdgeInsets.zero,
          title: Text(eligibilityLine.name),
          subtitle:
              Text(eligibilityLine.reason ?? l10n.returnWizardLineIneligible),
          trailing: const Icon(Icons.lock_outline),
        ),
      );
    }
    final selected = draft?.selected ?? false;
    return Card(
      margin: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.sm),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Checkbox(
                  value: selected,
                  onChanged: (v) => context.read<ReturnWizardBloc>().add(
                        ReturnWizardLineSelected(
                          productId: eligibilityLine.productId,
                          selected: v ?? false,
                        ),
                      ),
                ),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(eligibilityLine.name,
                          style: Theme.of(context).textTheme.bodyLarge),
                      Text(
                        l10n.returnWizardLineQty(eligibilityLine.qty),
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                    ],
                  ),
                ),
              ],
            ),
            if (selected)
              Padding(
                // Directional inset — flips to right-side in RTL so
                // the reason picker stays indented under the checkbox
                // column in Arabic. CodeRabbit feedback.
                padding: const EdgeInsetsDirectional.only(
                    start: AppSpacing.lg, top: AppSpacing.sm),
                child: _ReasonPicker(
                  productId: eligibilityLine.productId,
                  active: draft?.reason,
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _ReasonPicker extends StatelessWidget {
  const _ReasonPicker({required this.productId, required this.active});
  final String productId;
  final String? active;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Wrap(
      spacing: AppSpacing.sm,
      runSpacing: AppSpacing.sm,
      children: [
        for (final r in kReturnReasonFallback)
          ChoiceChip(
            label: Text(_reasonLabel(l10n, r.code)),
            selected: active == r.code,
            onSelected: (_) => context.read<ReturnWizardBloc>().add(
                  ReturnWizardLineReasonChanged(
                    productId: productId,
                    reason: r.code,
                  ),
                ),
          ),
      ],
    );
  }

  String _reasonLabel(AppLocalizations l10n, String code) {
    return switch (code) {
      'wrong_item' => l10n.returnReasonWrongItem,
      'damaged' => l10n.returnReasonDamaged,
      'not_as_described' => l10n.returnReasonNotAsDescribed,
      'other' => l10n.returnReasonOther,
      _ => code,
    };
  }
}

class _PhotoStrip extends StatelessWidget {
  const _PhotoStrip({required this.photos});
  final List<ReturnPhotoTile> photos;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Row(
      children: [
        OutlinedButton.icon(
          icon: const Icon(Icons.add_a_photo_outlined),
          label: Text(l10n.returnWizardAddPhoto),
          onPressed: () => _pickPhoto(context),
        ),
        const SizedBox(width: AppSpacing.md),
        Expanded(
          child: SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            child: Row(
              children: [
                for (final tile in photos)
                  PhotoUploadTile(
                    tile: tile,
                    onRetry: () => context.read<ReturnWizardBloc>().add(
                          ReturnWizardPhotoRetried(tile.clientPhotoKey),
                        ),
                    onRemove: () => context.read<ReturnWizardBloc>().add(
                          ReturnWizardPhotoCancelled(tile.clientPhotoKey),
                        ),
                  ),
              ],
            ),
          ),
        ),
      ],
    );
  }

  Future<void> _pickPhoto(BuildContext context) async {
    final bloc = context.read<ReturnWizardBloc>();
    final messenger = ScaffoldMessenger.maybeOf(context);
    final l10n = AppLocalizations.of(context);
    final picker = ImagePicker();
    try {
      final picked = await picker.pickImage(
        source: ImageSource.gallery,
        imageQuality: 85,
      );
      if (picked == null) return;
      final bytes = await picked.readAsBytes();
      bloc.add(
        ReturnWizardPhotoAddRequested(bytes: bytes, filename: picked.name),
      );
    } on Object catch (e) {
      // CodeRabbit feedback: image_picker can throw on permission
      // denial, platform errors, or transient file-read failures.
      // Surface a snack-bar rather than crash the wizard.
      // ignore: avoid_print
      print('_pickPhoto failed: $e');
      messenger?.showSnackBar(
        SnackBar(content: Text(l10n.returnWizardPhotoFailed)),
      );
    }
  }
}

class _RefundMethodPicker extends StatelessWidget {
  const _RefundMethodPicker({required this.active});
  final String? active;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(l10n.returnRefundMethodLabel,
            style: Theme.of(context).textTheme.titleSmall),
        const SizedBox(height: AppSpacing.sm),
        // The picker is optional — leaving every chip unselected sends
        // `preferredRefundMethod=null` and the server resolves the
        // default per market. Tapping a selected chip clears it.
        Wrap(
          spacing: AppSpacing.sm,
          runSpacing: AppSpacing.sm,
          children: [
            _chip(context, 'original_payment',
                l10n.returnRefundMethodOriginalPayment),
            _chip(
                context, 'bank_transfer', l10n.returnRefundMethodBankTransfer),
            _chip(context, 'wallet', l10n.returnRefundMethodWallet),
          ],
        ),
      ],
    );
  }

  Widget _chip(BuildContext context, String code, String label) {
    final isSelected = active == code;
    return ChoiceChip(
      label: Text(label),
      selected: isSelected,
      // Tapping a selected chip clears the choice — server then picks
      // the per-market default.
      onSelected: (_) => context
          .read<ReturnWizardBloc>()
          .add(ReturnWizardRefundMethodChanged(isSelected ? null : code)),
    );
  }
}
