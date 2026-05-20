import 'dart:async';

import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:mobile_scanner/mobile_scanner.dart';
import 'package:permission_handler/permission_handler.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/lookup_bloc.dart';

/// S-3.4 — SKU / barcode lookup screen. Camera permission is requested
/// only on Scan tap (BR-5 / AC) and the scanner is disposed when the
/// screen leaves the matched/no-match/scan states (plan.md "Barcode scan
/// freezes" troubleshooting note).
class LookupScreen extends StatefulWidget {
  const LookupScreen({super.key, this.permissionProbe});

  /// Injectable hook so widget tests can stub camera permission without
  /// pulling `permission_handler`'s platform channel into a unit test.
  /// Defaults to the real `Permission.camera.request()`.
  final Future<bool> Function()? permissionProbe;

  @override
  State<LookupScreen> createState() => _LookupScreenState();
}

class _LookupScreenState extends State<LookupScreen> {
  final TextEditingController _controller = TextEditingController();
  MobileScannerController? _scannerController;

  Future<bool> _probe() {
    final injected = widget.permissionProbe;
    if (injected != null) return injected();
    return Permission.camera
        .request()
        .then((s) => s.isGranted || s.isLimited);
  }

  Future<void> _onScanTap(BuildContext context) async {
    final granted = await _probe();
    if (!context.mounted) return;
    context
        .read<LookupBloc>()
        .add(LookupScanRequested(permissionGranted: granted));
    if (granted) {
      unawaited(_scannerController?.dispose());
      _scannerController = MobileScannerController();
    }
  }

  void _onScanCancel(BuildContext context) {
    unawaited(_scannerController?.dispose());
    _scannerController = null;
    context.read<LookupBloc>().add(const LookupScanCancelled());
  }

  @override
  void dispose() {
    _controller.dispose();
    unawaited(_scannerController?.dispose());
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.lookupTitle)),
      body: BlocConsumer<LookupBloc, LookupState>(
        listener: (context, state) {
          if (state is LookupMatched) {
            // Auto-route to PDP on successful match (BR-5 / S-3.4 AC).
            // Pop the lookup screen first so back from PDP returns to
            // the previous surface, not back to a matched-state lookup.
            context.go('/p/${state.slug}');
          }
        },
        builder: (context, state) {
          return switch (state) {
            LookupForm() => _FormBody(
                controller: _controller,
                onScan: () => _onScanTap(context),
              ),
            LookupScanning() => _ScanningBody(
                controller: _scannerController,
                onCancel: () => _onScanCancel(context),
              ),
            LookupLooking() => const Center(
                child: Padding(
                  padding: EdgeInsets.all(AppSpacing.lg),
                  child: CircularProgressIndicator(),
                ),
              ),
            LookupMatched(:final name) => _MatchedBody(name: name),
            LookupNoMatch() => const _NoMatchBody(),
            LookupPermissionDenied() => const _PermissionDeniedBody(),
            LookupFailure(:final reason, :final correlationId) => ErrorState(
                title: l10n.commonErrorTitle,
                body:
                    '$reason${correlationId == null ? '' : ' · $correlationId'}',
                onRetry: () =>
                    context.read<LookupBloc>().add(const LookupStarted()),
                retryLabel: l10n.commonRetry,
              ),
          };
        },
      ),
    );
  }
}

class _FormBody extends StatelessWidget {
  const _FormBody({required this.controller, required this.onScan});
  final TextEditingController controller;
  final VoidCallback onScan;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Padding(
      padding: const EdgeInsets.all(AppSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          TextField(
            controller: controller,
            decoration: InputDecoration(
              labelText: l10n.lookupManualLabel,
              border: const OutlineInputBorder(),
            ),
            textInputAction: TextInputAction.search,
            onSubmitted: (v) {
              final t = v.trim();
              if (t.isEmpty) return;
              context
                  .read<LookupBloc>()
                  .add(LookupSubmitted(value: t, kind: 'sku'));
            },
          ),
          const SizedBox(height: AppSpacing.md),
          FilledButton(
            onPressed: () {
              final t = controller.text.trim();
              if (t.isEmpty) return;
              context
                  .read<LookupBloc>()
                  .add(LookupSubmitted(value: t, kind: 'sku'));
            },
            child: Text(l10n.lookupSubmit),
          ),
          const SizedBox(height: AppSpacing.lg),
          OutlinedButton.icon(
            onPressed: onScan,
            icon: const Icon(Icons.qr_code_scanner),
            label: Text(l10n.lookupScanCta),
          ),
        ],
      ),
    );
  }
}

class _ScanningBody extends StatelessWidget {
  const _ScanningBody({required this.controller, required this.onCancel});
  final MobileScannerController? controller;
  final VoidCallback onCancel;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Stack(
      children: [
        // mobile_scanner does not run in the flutter widget-test
        // environment; the screen renders a placeholder during tests so
        // the surrounding controls remain exercisable.
        if (controller != null && !kIsWeb)
          MobileScanner(
            controller: controller,
            onDetect: (capture) {
              final raw = capture.barcodes
                  .map((b) => b.rawValue)
                  .firstWhere((v) => v != null && v.isNotEmpty,
                      orElse: () => null);
              if (raw == null) return;
              context.read<LookupBloc>().add(LookupScanResult(raw));
            },
          )
        else
          const ColoredBox(color: Colors.black),
        Align(
          alignment: Alignment.bottomCenter,
          child: Padding(
            padding: const EdgeInsets.all(AppSpacing.lg),
            child: FilledButton.tonal(
              onPressed: onCancel,
              child: Text(l10n.lookupScanCancel),
            ),
          ),
        ),
      ],
    );
  }
}

class _MatchedBody extends StatelessWidget {
  const _MatchedBody({required this.name});
  final String name;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    // The listener auto-routes to PDP — this body is a transient render
    // shown during the navigator transition.
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.lg),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.check_circle_outline,
                size: 56, color: Colors.green),
            const SizedBox(height: AppSpacing.md),
            Text(l10n.lookupMatchedTitle(name),
                style: Theme.of(context).textTheme.titleMedium),
          ],
        ),
      ),
    );
  }
}

class _NoMatchBody extends StatelessWidget {
  const _NoMatchBody();

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.lg),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.search_off,
                size: 56, color: Theme.of(context).colorScheme.outline),
            const SizedBox(height: AppSpacing.md),
            Text(l10n.lookupNoMatchTitle,
                style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: AppSpacing.sm),
            Text(l10n.lookupNoMatchBody, textAlign: TextAlign.center),
            const SizedBox(height: AppSpacing.lg),
            Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                TextButton(
                  onPressed: () => context.read<LookupBloc>()
                      .add(const LookupStarted()),
                  child: Text(l10n.commonRetry),
                ),
                const SizedBox(width: AppSpacing.md),
                FilledButton(
                  onPressed: () => context.go('/search'),
                  child: Text(l10n.lookupGoToSearch),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _PermissionDeniedBody extends StatelessWidget {
  const _PermissionDeniedBody();

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.lg),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.no_photography_outlined,
                size: 56, color: Theme.of(context).colorScheme.outline),
            const SizedBox(height: AppSpacing.md),
            Text(l10n.lookupPermissionDeniedTitle,
                style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: AppSpacing.sm),
            Text(l10n.lookupPermissionDeniedBody,
                textAlign: TextAlign.center),
            const SizedBox(height: AppSpacing.lg),
            FilledButton(
              onPressed: openAppSettings,
              child: Text(l10n.lookupOpenSettings),
            ),
            const SizedBox(height: AppSpacing.md),
            TextButton(
              onPressed: () =>
                  context.read<LookupBloc>().add(const LookupStarted()),
              child: Text(l10n.commonCancel),
            ),
          ],
        ),
      ),
    );
  }
}
