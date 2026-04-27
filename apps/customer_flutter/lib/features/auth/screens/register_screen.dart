import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/register_bloc.dart';

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
      appBar: AppBar(),
      body: BlocConsumer<RegisterBloc, RegisterState>(
        listener: (context, state) {
          if (state is RegisterRequiresOtp) {
            context.go('/auth/otp');
          } else if (state is RegisterSuccess) {
            context.go('/');
          }
        },
        builder: (context, state) {
          return Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: Column(
              children: [
                AppTextField(label: l10n.authRegister, controller: _name),
                const SizedBox(height: AppSpacing.md),
                AppTextField(
                  label: l10n.authRegister,
                  controller: _email,
                  keyboardType: TextInputType.emailAddress,
                ),
                const SizedBox(height: AppSpacing.md),
                AppTextField(
                  label: l10n.authRegister,
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
                    child: Text(state.reasonCode,
                        style: const TextStyle(color: AppColors.danger)),
                  ),
              ],
            ),
          );
        },
      ),
    );
  }
}
