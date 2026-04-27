import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/login_bloc.dart';

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
      appBar: AppBar(),
      body: BlocConsumer<LoginBloc, LoginState>(
        listener: (context, state) {
          if (state is LoginRequiresOtp) {
            context.go('/auth/otp');
          } else if (state is LoginSuccess) {
            final next = widget.continueTo ?? '/';
            context.go(next);
          }
        },
        builder: (context, state) {
          return Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: Column(
              children: [
                AppTextField(
                  label: l10n.authSignIn,
                  controller: _email,
                  keyboardType: TextInputType.emailAddress,
                ),
                const SizedBox(height: AppSpacing.md),
                AppTextField(
                  label: l10n.authSignIn,
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
                  Text(state.reasonCode,
                      style: const TextStyle(color: AppColors.danger)),
                TextButton(
                  onPressed: () => context.go('/auth/reset'),
                  child: Text(l10n.authForgotPassword),
                ),
              ],
            ),
          );
        },
      ),
    );
  }
}
