import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../data/models/verification_models.dart';

/// Renders one schema-driven field (S-7.2). One widget per server-side
/// `type` — unknown types fall back to a plain text input per plan.md
/// "Dynamic form rendering" defensive fallback.
///
/// `doc` fields render as a read-only hint card — actual document
/// upload happens on the detail screen (S-7.3) after the case exists.
class SchemaFieldWidget extends StatelessWidget {
  const SchemaFieldWidget({
    super.key,
    required this.field,
    required this.value,
    required this.onChanged,
    this.errorKey,
  });

  final SchemaField field;
  final Object? value;
  final ValueChanged<Object?> onChanged;

  /// Optional localization key for the inline error. Resolved against
  /// `AppLocalizations` at render time so the bloc stays copy-free.
  final String? errorKey;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final errorText = _resolveError(l10n, errorKey);
    final label = field.required ? '${field.label} *' : field.label;

    switch (field.type) {
      case 'enum':
        return DropdownButtonFormField<String>(
          initialValue: value as String?,
          decoration: InputDecoration(labelText: label, errorText: errorText),
          items: field.options
              .map((opt) => DropdownMenuItem(value: opt, child: Text(opt)))
              .toList(growable: false),
          onChanged: onChanged,
        );
      case 'date':
        return _DateField(
          label: label,
          value: value as DateTime?,
          errorText: errorText,
          onChanged: (d) => onChanged(d?.toIso8601String()),
        );
      case 'number':
        return TextFormField(
          initialValue: value?.toString() ?? '',
          decoration: InputDecoration(labelText: label, errorText: errorText),
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          onChanged: onChanged,
        );
      case 'doc':
        return Card(
          color: AppColors.neutral,
          child: Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: Row(
              children: [
                const Icon(Icons.upload_file_outlined, size: 20),
                const SizedBox(width: AppSpacing.sm),
                Expanded(
                  child: Text(
                    '${field.label} — ${l10n.verificationDetailUploadCta}',
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ),
              ],
            ),
          ),
        );
      case 'text':
      default:
        // Defensive fallback (plan §"Dynamic form rendering") —
        // unknown future server types render as plain text inputs so
        // the form remains usable.
        return TextFormField(
          initialValue: value?.toString() ?? '',
          decoration: InputDecoration(labelText: label, errorText: errorText),
          onChanged: onChanged,
        );
    }
  }

  String? _resolveError(AppLocalizations l10n, String? key) {
    if (key == null) return null;
    switch (key) {
      case 'verificationSubmitRequiredHint':
        return l10n.verificationSubmitRequiredHint;
      case 'verificationSubmitErrorPattern':
        return l10n.verificationSubmitErrorPattern;
      default:
        // Server-side validation errors arrive verbatim; surface them.
        return key;
    }
  }
}

class _DateField extends StatelessWidget {
  const _DateField({
    required this.label,
    required this.value,
    required this.onChanged,
    this.errorText,
  });

  final String label;
  final DateTime? value;
  final ValueChanged<DateTime?> onChanged;
  final String? errorText;

  @override
  Widget build(BuildContext context) {
    final locale = Localizations.localeOf(context).toString();
    final dateFmt = DateFormat.yMMMd(locale);
    return InkWell(
      onTap: () async {
        final now = DateTime.now();
        final picked = await showDatePicker(
          context: context,
          firstDate: DateTime(1900),
          lastDate: now,
          initialDate: value ?? now,
        );
        if (picked != null) onChanged(picked);
      },
      child: InputDecorator(
        decoration: InputDecoration(labelText: label, errorText: errorText),
        child: Text(
          value == null ? '' : dateFmt.format(value!),
          style: Theme.of(context).textTheme.bodyMedium,
        ),
      ),
    );
  }
}
