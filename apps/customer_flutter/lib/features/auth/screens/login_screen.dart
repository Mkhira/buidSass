import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/login_bloc.dart';
import '../services/auth_error_messages.dart';

class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key, this.continueTo});
  final String? continueTo;

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _email = TextEditingController();
  final _password = TextEditingController();

  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Scaffold(
      appBar: AppBar(title: Text(l10n.authSignIn)),
      body: BlocConsumer<LoginBloc, LoginState>(
        listener: (context, state) {
          if (state is LoginRequiresOtp) {
            final qs = Uri(queryParameters: {
              'challengeId': state.challenge.challengeId,
              'channel': state.challenge.channel,
              'retryAfter': state.challenge.retryAfterSeconds.toString(),
              if (widget.continueTo != null) 'continueTo': widget.continueTo!,
            }).query;
            context.go('/auth/otp?$qs');
          }
          // LoginSuccess: the router redirect re-evaluates on auth state
          // change and honours `?continueTo=…` from the URL — no manual
          // navigation here.
        },
        builder: (context, state) {
          return Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: Column(
              children: [
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
                  label: l10n.authSignIn,
                  expand: true,
                  isLoading: state is LoginSubmitting,
                  onPressed: () => context.read<LoginBloc>().add(
                        LoginSubmitted(
                          email: _email.text.trim(),
                          password: _password.text,
                        ),
                      ),
                ),
                const SizedBox(height: AppSpacing.sm),
                if (state is LoginFailure)
                  Text(
                    localizeAuthError(l10n, state.reasonCode),
                    style: const TextStyle(color: AppColors.danger),
                  ),
                TextButton(
                  onPressed: () => context.go('/auth/reset'),
                  child: Text(l10n.authForgotPassword),
                ),
                TextButton(
                  onPressed: () => context.go('/auth/register'),
                  child: Text(l10n.authRegister),
                ),
              ],
            ),
          );
        },
      ),
    );
  }
}
