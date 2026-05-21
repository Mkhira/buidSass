import 'package:flutter/material.dart';

import '../../../generated/l10n/app_localizations.dart';

/// Maps a wire role enum to its localized label. Unknown future roles
/// surface as the raw value (defensive — admin actions are still gated
/// by `Company.isAdmin` on the server-confirmed role).
String roleLabel(BuildContext context, String role) {
  final l10n = AppLocalizations.of(context);
  return switch (role) {
    'admin' => l10n.companyRoleAdmin,
    'buyer' => l10n.companyRoleBuyer,
    'approver' => l10n.companyRoleApprover,
    _ => role,
  };
}

/// Small dropdown for the 3-role enum used by invite + membership-edit
/// screens. Server is the source of truth on the enum values — UI
/// only renders the known three.
class RolePicker extends StatelessWidget {
  const RolePicker({
    super.key,
    required this.value,
    required this.onChanged,
    this.label,
  });

  final String value;
  final ValueChanged<String> onChanged;
  final String? label;

  @override
  Widget build(BuildContext context) {
    return DropdownButtonFormField<String>(
      initialValue: _knownRoles.contains(value) ? value : 'buyer',
      decoration: InputDecoration(labelText: label),
      items: [
        for (final r in _knownRoles)
          DropdownMenuItem(value: r, child: Text(roleLabel(context, r))),
      ],
      onChanged: (v) {
        if (v != null) onChanged(v);
      },
    );
  }
}

const _knownRoles = <String>['admin', 'approver', 'buyer'];
