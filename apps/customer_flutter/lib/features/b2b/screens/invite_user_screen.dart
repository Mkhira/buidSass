import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/invite_user_bloc.dart';
import '../widgets/role_picker.dart';

class InviteUserScreen extends StatelessWidget {
  const InviteUserScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.inviteUserTitle)),
      body: BlocConsumer<InviteUserBloc, InviteUserState>(
        listener: (context, state) {
          if (state is InviteUserDone) {
            final messenger = ScaffoldMessenger.maybeOf(context);
            messenger?.showSnackBar(
              SnackBar(content: Text(l10n.inviteUserSent)),
            );
            context.pop();
          }
        },
        builder: (context, state) {
          // Use the sealed type — the previous `as InviteUserForm` cast
          // crashed when the bloc emitted InviteUserDone (the listener
          // navigates async, but the builder still runs once on Done
          // before the route changes).
          final InviteUserForm form;
          final bool submitting;
          switch (state) {
            case InviteUserForm():
              form = state;
              submitting = false;
            case InviteUserSubmitting():
              form = state.form;
              submitting = true;
            case InviteUserDone():
              return LoadingState(semanticsLabel: l10n.commonLoading);
          }
          return SafeArea(
            child: Column(
              children: [
                if (form.formError != null)
                  Padding(
                    padding: const EdgeInsets.all(AppSpacing.md),
                    child: Container(
                      width: double.infinity,
                      padding: const EdgeInsets.all(AppSpacing.md),
                      decoration: BoxDecoration(
                        color: AppColors.danger.withValues(alpha: 0.1),
                        border: Border.all(color: AppColors.danger),
                        borderRadius: BorderRadius.circular(8),
                      ),
                      child: Text(
                        l10n.commonErrorBody,
                        style: const TextStyle(color: AppColors.danger),
                      ),
                    ),
                  ),
                Expanded(
                  child: ListView(
                    padding: const EdgeInsets.all(AppSpacing.md),
                    children: [
                      TextFormField(
                        initialValue: form.email,
                        enabled: !submitting,
                        keyboardType: TextInputType.emailAddress,
                        decoration: InputDecoration(
                          labelText: l10n.inviteUserEmailLabel,
                        ),
                        onChanged: (v) => context
                            .read<InviteUserBloc>()
                            .add(InviteUserEmailChanged(v)),
                      ),
                      const SizedBox(height: AppSpacing.md),
                      RolePicker(
                        value: form.role,
                        label: l10n.inviteUserRoleLabel,
                        onChanged: submitting
                            ? (_) {}
                            : (v) => context
                                .read<InviteUserBloc>()
                                .add(InviteUserRoleChanged(v)),
                      ),
                    ],
                  ),
                ),
                Padding(
                  padding: const EdgeInsets.all(AppSpacing.md),
                  child: AppButton(
                    label: l10n.inviteUserSubmitCta,
                    expand: true,
                    isLoading: submitting,
                    onPressed: submitting || !form.canSubmit
                        ? null
                        : () => context
                            .read<InviteUserBloc>()
                            .add(const InviteUserSubmitted()),
                  ),
                ),
              ],
            ),
          );
        },
      ),
    );
  }
}
