import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/otp_bloc.dart';

class OtpScreen extends StatefulWidget {
  const OtpScreen({super.key});

  @override
  State<OtpScreen> createState() => _OtpScreenState();
}

class _OtpScreenState extends State<OtpScreen> {
  final _code = TextEditingController();

  @override
  void dispose() {
    _code.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Scaffold(
      appBar: AppBar(),
      body: BlocConsumer<OtpBloc, OtpState>(
        listener: (context, state) {
          if (state.success) context.go('/');
        },
        builder: (context, state) {
          return Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: Column(
              children: [
                Text(l10n.authOtpTitle, style: AppTypography.headline),
                const SizedBox(height: AppSpacing.lg),
                AppTextField(
                  label: l10n.authOtpTitle,
                  controller: _code,
                  keyboardType: TextInputType.number,
                  autofillHints: const [AutofillHints.oneTimeCode],
                ),
                const SizedBox(height: AppSpacing.md),
                AppButton(
                  label: l10n.commonContinue,
                  expand: true,
                  isLoading: state.submitting,
                  onPressed: () =>
                      context.read<OtpBloc>().add(OtpSubmitted(_code.text.trim())),
                ),
                const SizedBox(height: AppSpacing.sm),
                TextButton(
                  onPressed: state.resendInSeconds > 0
                      ? null
                      : () => context.read<OtpBloc>().add(const OtpResendRequested()),
                  child: Text(state.resendInSeconds > 0
                      ? '${l10n.authResendOtp} (${state.resendInSeconds})'
                      : l10n.authResendOtp),
                ),
                if (state.errorReason != null)
                  Text(state.errorReason!,
                      style: const TextStyle(color: AppColors.danger)),
              ],
            ),
          );
        },
      ),
    );
  }
}

