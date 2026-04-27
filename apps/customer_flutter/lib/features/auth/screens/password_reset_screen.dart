import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/password_reset_bloc.dart';
import '../services/auth_error_messages.dart';

class PasswordResetRequestScreen extends StatefulWidget {
  const PasswordResetRequestScreen({super.key});

  @override
  State<PasswordResetRequestScreen> createState() =>
      _PasswordResetRequestScreenState();
}

class _PasswordResetRequestScreenState
    extends State<PasswordResetRequestScreen> {
  final _email = TextEditingController();

  @override
  void dispose() {
    _email.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Scaffold(
      appBar: AppBar(title: Text(l10n.authForgotPassword)),
      body: BlocBuilder<PasswordResetBloc, PasswordResetState>(
        builder: (context, state) {
          return Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: Column(
              children: [
                AppTextField(
                  label: l10n.authResetEmailLabel,
                  controller: _email,
                  keyboardType: TextInputType.emailAddress,
                ),
                const SizedBox(height: AppSpacing.md),
                AppButton(
                  label: l10n.commonContinue,
                  expand: true,
                  isLoading: state is PasswordResetSubmitting,
                  onPressed: () => context.read<PasswordResetBloc>().add(
                        PasswordResetEmailRequested(_email.text.trim()),
                      ),
                ),
                if (state is PasswordResetEmailSent)
                  Padding(
                    padding: const EdgeInsets.only(top: AppSpacing.md),
                    child: Text(l10n.authResetEmailSent),
                  ),
                if (state is PasswordResetFailure)
                  Padding(
                    padding: const EdgeInsets.only(top: AppSpacing.sm),
                    child: Text(
                      localizeAuthError(l10n, state.reasonCode),
                      style: const TextStyle(color: AppColors.danger),
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

class PasswordResetConfirmScreen extends StatefulWidget {
  const PasswordResetConfirmScreen({super.key, required this.token});
  final String token;

  @override
  State<PasswordResetConfirmScreen> createState() =>
      _PasswordResetConfirmScreenState();
}

class _PasswordResetConfirmScreenState
    extends State<PasswordResetConfirmScreen> {
  final _password = TextEditingController();

  @override
  void dispose() {
    _password.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Scaffold(
      appBar: AppBar(title: Text(l10n.authForgotPassword)),
      body: BlocBuilder<PasswordResetBloc, PasswordResetState>(
        builder: (context, state) {
          return Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: Column(
              children: [
                AppTextField(
                  label: l10n.authNewPasswordLabel,
                  controller: _password,
                  obscureText: true,
                ),
                const SizedBox(height: AppSpacing.md),
                AppButton(
                  label: l10n.commonSave,
                  expand: true,
                  isLoading: state is PasswordResetSubmitting,
                  onPressed: () => context.read<PasswordResetBloc>().add(
                        PasswordResetConfirmSubmitted(
                          token: widget.token,
                          newPassword: _password.text,
                        ),
                      ),
                ),
                if (state is PasswordResetConfirmed)
                  Padding(
                    padding: const EdgeInsets.only(top: AppSpacing.md),
                    child: Text(l10n.authPasswordSaved),
                  ),
                if (state is PasswordResetFailure)
                  Padding(
                    padding: const EdgeInsets.only(top: AppSpacing.sm),
                    child: Text(
                      localizeAuthError(l10n, state.reasonCode),
                      style: const TextStyle(color: AppColors.danger),
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
