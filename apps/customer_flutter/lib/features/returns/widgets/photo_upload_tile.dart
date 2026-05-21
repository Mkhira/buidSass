import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/return_wizard_bloc.dart';

/// Per-tile photo widget for the return wizard. Renders one of the
/// three `ReturnPhotoTileStatus` states — uploading (with spinner),
/// ready (thumbnail + remove), failed (retry CTA).
class PhotoUploadTile extends StatelessWidget {
  const PhotoUploadTile({
    super.key,
    required this.tile,
    required this.onRetry,
    required this.onRemove,
  });

  final ReturnPhotoTile tile;
  final VoidCallback onRetry;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Container(
      width: 96,
      height: 96,
      // Directional inset — RTL flips this to the start-edge so the
      // tile gap stays on the trailing side in Arabic. CodeRabbit
      // feedback.
      margin: const EdgeInsetsDirectional.only(end: AppSpacing.sm),
      decoration: BoxDecoration(
        color: AppColors.neutral,
        borderRadius: BorderRadius.circular(8),
      ),
      child: Stack(
        children: [
          // Thumbnail (always, even while uploading) — bytes are local.
          ClipRRect(
            borderRadius: BorderRadius.circular(8),
            child: Image.memory(
              tile.bytes,
              width: 96,
              height: 96,
              fit: BoxFit.cover,
              errorBuilder: (_, __, ___) => const Icon(Icons.image_outlined),
            ),
          ),
          if (tile.status == ReturnPhotoTileStatus.uploading)
            Container(
              decoration: BoxDecoration(
                color: Colors.black.withValues(alpha: 0.45),
                borderRadius: BorderRadius.circular(8),
              ),
              alignment: Alignment.center,
              child: Semantics(
                label: l10n.returnWizardPhotoUploading,
                child: const SizedBox(
                  width: 24,
                  height: 24,
                  child: CircularProgressIndicator(
                    color: Colors.white,
                    strokeWidth: 2,
                  ),
                ),
              ),
            ),
          if (tile.status == ReturnPhotoTileStatus.failed)
            Positioned.fill(
              child: Material(
                color: AppColors.danger.withValues(alpha: 0.65),
                borderRadius: BorderRadius.circular(8),
                child: InkWell(
                  borderRadius: BorderRadius.circular(8),
                  onTap: onRetry,
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      const Icon(Icons.refresh, color: Colors.white),
                      Text(
                        l10n.returnWizardRetryPhoto,
                        textAlign: TextAlign.center,
                        style: const TextStyle(
                          color: Colors.white,
                          fontSize: 10,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          if (tile.status != ReturnPhotoTileStatus.uploading)
            // PositionedDirectional flips `end` to the start edge in
            // RTL — keeps the X chip on the trailing-top corner in
            // both locales. CodeRabbit feedback.
            PositionedDirectional(
              top: 0,
              end: 0,
              // 44×44 tap target per WCAG 2.5.5 + CodeRabbit loop 2
              // follow-up. The InkWell itself fills the full 44×44 so
              // a tap anywhere in that region fires `onRemove`; the
              // visible circular chip is decorative-only via
              // `AlignmentDirectional.topEnd` inside the ink response.
              child: SizedBox(
                width: 44,
                height: 44,
                child: Material(
                  type: MaterialType.transparency,
                  child: InkWell(
                    onTap: onRemove,
                    child: Align(
                      alignment: AlignmentDirectional.topEnd,
                      child: Container(
                        padding: const EdgeInsets.all(4),
                        decoration: BoxDecoration(
                          color: Colors.black.withValues(alpha: 0.55),
                          shape: BoxShape.circle,
                        ),
                        child: Semantics(
                          button: true,
                          label: l10n.returnWizardRemovePhoto,
                          child: const Icon(
                            Icons.close,
                            color: Colors.white,
                            size: 18,
                          ),
                        ),
                      ),
                    ),
                  ),
                ),
              ),
            ),
        ],
      ),
    );
  }
}
