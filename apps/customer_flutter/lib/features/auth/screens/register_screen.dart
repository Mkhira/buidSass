import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/register_bloc.dart';
import '../services/auth_error_messages.dart';

class RegisterScreen extends StatefulWidget {
  const RegisterScreen({super.key});

  @override
  State<RegisterScreen> createState() => _RegisterScreenState();
}

class _RegisterScreenState extends State<RegisterScreen> {
  final _email = TextEditingController();
  final _password = TextEditingController();
  final _name = TextEditingController();

  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
    _name.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Scaffold(
      appBar: AppBar(title: Text(l10n.authRegister)),
      body: BlocConsumer<RegisterBloc, RegisterState>(
        listener: (context, state) {
          if (state is RegisterRequiresOtp) {
            final qs = Uri(queryParameters: {
              'challengeId': state.challenge.challengeId,
              'channel': state.challenge.channel,
              'retryAfter': state.challenge.retryAfterSeconds.toString(),
            }).query;
            context.go('/auth/otp?$qs');
          }
          // RegisterSuccess: rely on the router redirect.
        },
        builder: (context, state) {
          return Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: Column(
              children: [
                AppTextField(label: l10n.authNameLabel, controller: _name),
                const SizedBox(height: AppSpacing.md),
                AppTextField(
                  label: l10n.authEmailLabel,
                  controller: _email,
                  keyboardType: TextInputType.emailAddress,
                ),
                const SizedBox(height: AppSpacing.md),
                AppTextField(
                  label: l10n.authPasswordLabel,
                  controller: _password,
                  obscureText: true,
                ),
                const SizedBox(height: AppSpacing.lg),
                AppButton(
                  label: l10n.authRegister,
                  expand: true,
                  isLoading: state is RegisterSubmitting,
                  onPressed: () => context.read<RegisterBloc>().add(
                        RegisterSubmitted(
                          email: _email.text.trim(),
                          password: _password.text,
                          displayName: _name.text.trim(),
                        ),
                      ),
                ),
                if (state is RegisterFailure)
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
