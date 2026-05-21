import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../data/models/quote_models.dart';

/// Vertical timeline of quote versions (S-8.5). The latest version is
/// rendered solid; older versions are faded so the user understands
/// pricing/terms have evolved.
class QuoteVersionTimeline extends StatelessWidget {
  const QuoteVersionTimeline({
    super.key,
    required this.versions,
    this.selectedVersionId,
    this.onSelected,
  });

  final List<QuoteVersion> versions;
  final String? selectedVersionId;
  final ValueChanged<String>? onSelected;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toString();
    final dateFmt = DateFormat.yMMMd(locale);
    if (versions.isEmpty) return const SizedBox.shrink();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          l10n.quoteDetailVersionsLabel,
          style: Theme.of(context).textTheme.titleSmall,
        ),
        const SizedBox(height: AppSpacing.sm),
        for (var i = 0; i < versions.length; i++) ...[
          _Row(
            label: l10n.quoteDetailVersionLabel(i + 1),
            publishedLabel: l10n.quoteDetailPublishedAt(
              dateFmt.format(versions[i].publishedAt.toLocal()),
            ),
            selected: (selectedVersionId ?? versions.last.versionId) ==
                versions[i].versionId,
            latest: i == versions.length - 1,
            onTap: onSelected == null
                ? null
                : () => onSelected!(versions[i].versionId),
          ),
          if (i < versions.length - 1)
            const Padding(
              padding: EdgeInsets.symmetric(horizontal: 8),
              child: SizedBox(
                height: 16,
                child:
                    VerticalDivider(width: 2, color: AppColors.textSecondary),
              ),
            ),
        ],
      ],
    );
  }
}

class _Row extends StatelessWidget {
  const _Row({
    required this.label,
    required this.publishedLabel,
    required this.selected,
    required this.latest,
    this.onTap,
  });

  final String label;
  final String publishedLabel;
  final bool selected;
  final bool latest;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: onTap,
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
        child: Row(
          children: [
            Icon(
              latest ? Icons.circle : Icons.circle_outlined,
              size: 12,
              color: latest ? AppColors.success : AppColors.textSecondary,
            ),
            const SizedBox(width: AppSpacing.sm),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    label,
                    style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                          fontWeight:
                              selected ? FontWeight.w700 : FontWeight.w500,
                        ),
                  ),
                  Text(
                    publishedLabel,
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ],
              ),
            ),
            if (selected)
              const Icon(Icons.check, size: 18, color: AppColors.success),
          ],
        ),
      ),
    );
  }
}
