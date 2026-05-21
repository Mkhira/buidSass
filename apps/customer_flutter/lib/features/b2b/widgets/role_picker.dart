import 'package:flutter/material.dart';

import '../../../generated/l10n/app_localizations.dart';

/// Maps a wire role enum to its localized label. Unknown future
/// server-added roles render as a localized "Member" rather than the
/// raw wire string — keeps Arabic surfaces editorial-grade even if a
/// new role appears (admin actions stay gated by `Company.isAdmin` on
/// the server-confirmed role).
String roleLabel(BuildContext context, String role) {
  final l10n = AppLocalizations.of(context);
  return switch (role) {
    'admin' => l10n.companyRoleAdmin,
    'buyer' => l10n.companyRoleBuyer,
    'approver' => l10n.companyRoleApprover,
    _ => l10n.companyRoleUnknown,
  };
}

/// Small dropdown for the 3-role enum used by invite + membership-edit
/// screens. If the inbound value is a server-side role we don't know
/// about, we surface it as a (selected) entry in the dropdown so
/// changing the role doesn't silently coerce the user from a new
/// server role into `buyer`.
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
    final isKnown = _knownRoles.contains(value);
    final entries = [
      if (!isKnown) value,
      ..._knownRoles,
    ];
    return DropdownButtonFormField<String>(
      initialValue: value,
      decoration: InputDecoration(labelText: label),
      items: [
        for (final r in entries)
          DropdownMenuItem(value: r, child: Text(roleLabel(context, r))),
      ],
      onChanged: (v) {
        if (v != null) onChanged(v);
      },
    );
  }
}

const _knownRoles = <String>['admin', 'approver', 'buyer'];
