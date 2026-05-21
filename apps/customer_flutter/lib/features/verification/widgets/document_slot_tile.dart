import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/verification_detail_bloc.dart';

typedef DocumentPicker = Future<({Uint8List bytes, String filename})?>
    Function();

/// Per-slot tile (S-7.3). Shows the current upload state and offers
/// pick / retry. Once the slot has a server-side document URL, it
/// renders as uploaded with the filename masked (we don't expose the
/// signed URL beyond a tap-to-preview affordance).
class DocumentSlotTile extends StatelessWidget {
  const DocumentSlotTile({
    super.key,
    required this.slotKey,
    required this.slotLabel,
    required this.uploadState,
    required this.alreadyUploaded,
    required this.onPick,
    required this.onRetry,
    this.required = false,
    this.requestedNote,
  });

  final String slotKey;
  final String slotLabel;
  final SlotUploadState uploadState;
  final bool alreadyUploaded;
  final DocumentPicker onPick;
  final VoidCallback onRetry;
  // ignore: non_constant_identifier_names
  final bool required;
  final String? requestedNote;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final status = uploadState.status;

    Widget statusLine;
    Widget? action;
    switch (status) {
      case SlotUploadStatus.uploading:
        statusLine = Row(
          children: [
            const SizedBox(
              width: 14,
              height: 14,
              child: CircularProgressIndicator(strokeWidth: 2),
            ),
            const SizedBox(width: AppSpacing.sm),
            Text(l10n.verificationDetailUploadInProgress),
          ],
        );
        action = null;
        break;
      case SlotUploadStatus.failed:
        statusLine = Text(
          uploadState.errorMessage ?? l10n.commonErrorBody,
          style: const TextStyle(color: AppColors.danger),
        );
        action = TextButton(
          onPressed: onRetry,
          child: Text(l10n.verificationDetailUploadRetryCta),
        );
        break;
      case SlotUploadStatus.ready:
        statusLine = Row(
          children: [
            const Icon(Icons.check_circle, size: 18, color: AppColors.success),
            const SizedBox(width: AppSpacing.sm),
            Text(l10n.verificationDetailUploaded),
          ],
        );
        action = null;
        break;
      case SlotUploadStatus.idle:
        if (alreadyUploaded) {
          statusLine = Row(
            children: [
              const Icon(Icons.check_circle,
                  size: 18, color: AppColors.success),
              const SizedBox(width: AppSpacing.sm),
              Text(l10n.verificationDetailUploaded),
            ],
          );
        } else {
          statusLine = Text(
            requestedNote ?? '',
            style: Theme.of(context).textTheme.bodySmall,
          );
        }
        action = Builder(
          builder: (innerContext) {
            return TextButton.icon(
              icon: const Icon(Icons.upload, size: 18),
              label: Text(l10n.verificationDetailUploadCta),
              onPressed: () async {
                // Capture the dispatcher before the async gap so we
                // don't need a `mounted` check after the picker
                // completes. The dispatcher reference stays valid as
                // long as the tile remains in the tree.
                final dispatcher = DocumentSlotDispatcher.of(innerContext);
                final picked = await onPick();
                if (picked != null) {
                  dispatcher.onPicked(slotKey, picked);
                }
              },
            );
          },
        );
        break;
    }

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
                    slotLabel,
                    style: Theme.of(context).textTheme.titleSmall,
                  ),
                ),
                if (required)
                  Text(
                    l10n.verificationSubmitRequiredHint,
                    style: Theme.of(context).textTheme.bodySmall?.copyWith(
                          color: AppColors.warning,
                        ),
                  ),
              ],
            ),
            const SizedBox(height: AppSpacing.xs),
            statusLine,
            if (action != null) ...[
              const SizedBox(height: AppSpacing.xs),
              action,
            ],
          ],
        ),
      ),
    );
  }
}

/// Tiny side-channel: the tile calls back to the screen through this
/// dispatcher so the test seam is a typed function rather than a
/// global. The screen wires this to the detail bloc.
class DocumentSlotDispatcher extends InheritedWidget {
  const DocumentSlotDispatcher({
    super.key,
    required this.onPicked,
    required super.child,
  });

  final void Function(String slotKey, ({Uint8List bytes, String filename}) picked)
      onPicked;

  static DocumentSlotDispatcher of(BuildContext context) {
    final d = context
        .dependOnInheritedWidgetOfExactType<DocumentSlotDispatcher>();
    assert(d != null, 'No DocumentSlotDispatcher above this widget');
    return d!;
  }

  @override
  bool updateShouldNotify(DocumentSlotDispatcher oldWidget) => false;
}

/// Default file picker: uses `image_picker` (gallery only — camera path
/// is gated by the device camera permission flow). Returns null if the
/// user cancels.
Future<({Uint8List bytes, String filename})?> pickDocumentImage() async {
  final picker = ImagePicker();
  final picked = await picker.pickImage(
    source: ImageSource.gallery,
    imageQuality: 85,
  );
  if (picked == null) return null;
  final bytes = await picked.readAsBytes();
  return (bytes: bytes, filename: picked.name);
}
