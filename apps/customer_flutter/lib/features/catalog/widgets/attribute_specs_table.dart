import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

class AttributeSpecsTable extends StatelessWidget {
  const AttributeSpecsTable({super.key, required this.attributes});
  final Map<String, String> attributes;

  @override
  Widget build(BuildContext context) {
    if (attributes.isEmpty) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.all(AppSpacing.md),
      child: Column(
        children: attributes.entries.map((e) {
          return Padding(
            padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  flex: 2,
                  child: Text(e.key,
                      style: AppTypography.body
                          .copyWith(color: AppColors.textSecondary)),
                ),
                Expanded(
                  flex: 3,
                  child: Text(e.value, style: AppTypography.body),
                ),
              ],
            ),
          );
        }).toList(growable: false),
      ),
    );
  }
}
