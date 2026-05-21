import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../data/models/company_models.dart';
import 'role_picker.dart';

/// One member row for the company memberships screen. Admin-only
/// actions (role change, remove) are gated on [canManage]; when false
/// the row renders as read-only.
class MemberRow extends StatelessWidget {
  const MemberRow({
    super.key,
    required this.member,
    required this.canManage,
    this.onRoleChanged,
    this.onRemoveRequested,
    this.busy = false,
  });

  final Membership member;
  final bool canManage;
  final ValueChanged<String>? onRoleChanged;
  final VoidCallback? onRemoveRequested;

  /// Locks both the role-picker and the remove button while the bloc
  /// is processing a transition for THIS member.
  final bool busy;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.md),
        child: Row(
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    member.name,
                    style: Theme.of(context).textTheme.titleSmall,
                  ),
                  const SizedBox(height: AppSpacing.xs),
                  if (canManage && onRoleChanged != null)
                    SizedBox(
                      width: 180,
                      child: RolePicker(
                        value: member.role,
                        onChanged: busy ? (_) {} : onRoleChanged!,
                        label: l10n.inviteUserRoleLabel,
                      ),
                    )
                  else
                    Text(
                      roleLabel(context, member.role),
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                ],
              ),
            ),
            if (canManage && onRemoveRequested != null) ...[
              const SizedBox(width: AppSpacing.sm),
              IconButton(
                tooltip: l10n.membershipsRemoveCta,
                icon: const Icon(Icons.delete_outline, color: AppColors.danger),
                onPressed: busy ? null : onRemoveRequested,
              ),
            ],
          ],
        ),
      ),
    );
  }
}
