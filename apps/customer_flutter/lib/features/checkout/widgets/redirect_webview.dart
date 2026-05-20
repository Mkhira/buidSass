import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

import '../../../generated/l10n/app_localizations.dart';

/// Placeholder for the 3DS / provider-WebView return handler (T-4.16).
/// The V1 review-bloc bypasses this widget and emits success directly;
/// real WebView integration lands when provider sandbox credentials are
/// wired (HyperPay/Tap/Tabby/Tamara/etc., per ADR-007).
///
/// Lives in the repo so deep-link wiring + the bloc state surface
/// (`CheckoutReviewRedirecting`) compile against the right import path,
/// and so a follow-up PR can swap in the real implementation without
/// changing the router.
class RedirectWebView extends StatelessWidget {
  const RedirectWebView({
    super.key,
    required this.url,
    required this.onCompleted,
    required this.onCancelled,
  });

  final String url;
  final ValueChanged<bool> onCompleted;
  final VoidCallback onCancelled;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(
        title: const Text('3DS / provider'),
        leading: IconButton(
          icon: const Icon(Icons.close),
          onPressed: onCancelled,
        ),
      ),
      body: Center(
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            const Icon(Icons.web, size: 64),
            const SizedBox(height: AppSpacing.md),
            // Stub copy — intentional. Real WebView lands in the next
            // PR per Phase 4 plan.md "Provider WebView return" risk #3.
            Text('Provider redirect stub — $url'),
            const SizedBox(height: AppSpacing.lg),
            FilledButton(
              onPressed: () => onCompleted(true),
              child: Text(l10n.commonContinue),
            ),
            TextButton(
              onPressed: onCancelled,
              child: Text(l10n.commonCancel),
            ),
          ],
        ),
      ),
    );
  }
}
