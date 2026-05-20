import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/checkout_address_bloc.dart';
import '../bloc/checkout_drift.dart';
import '../data/models/checkout_models.dart';
import '../widgets/conflict_dialog.dart';

class AddressStepScreen extends StatefulWidget {
  const AddressStepScreen({super.key, required this.sessionId});
  final String sessionId;

  @override
  State<AddressStepScreen> createState() => _AddressStepScreenState();
}

class _AddressStepScreenState extends State<AddressStepScreen> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _name;
  late final TextEditingController _phone;
  late final TextEditingController _city;
  late final TextEditingController _region;
  late final TextEditingController _street;
  late final TextEditingController _postal;

  @override
  void initState() {
    super.initState();
    // The bloc usually mounts in CheckoutAddressForm, but a hot-reload or
    // a router re-build can land us here in any state. Read defensively.
    final s = context.read<CheckoutAddressBloc>().state;
    final initial = s is CheckoutAddressForm ? s.initial : null;
    _name = TextEditingController(text: initial?.name ?? '');
    _phone = TextEditingController(text: initial?.phone ?? '');
    _city = TextEditingController(text: initial?.city ?? '');
    _region = TextEditingController(text: initial?.region ?? '');
    _street = TextEditingController(text: initial?.street ?? '');
    _postal = TextEditingController(text: initial?.postalCode ?? '');
  }

  @override
  void dispose() {
    for (final c in [_name, _phone, _city, _region, _street, _postal]) {
      c.dispose();
    }
    super.dispose();
  }

  CheckoutAddressDto _buildDto() => CheckoutAddressDto(
        name: _name.text.trim(),
        phone: _phone.text.trim(),
        city: _city.text.trim(),
        region: _region.text.trim(),
        street: _street.text.trim(),
        postalCode: _postal.text.trim().isEmpty ? null : _postal.text.trim(),
      );

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return BlocConsumer<CheckoutAddressBloc, CheckoutAddressState>(
      listener: (context, state) async {
        if (state is CheckoutAddressSubmitted) {
          await context.push('/checkout/${widget.sessionId}/shipping');
        } else if (state is CheckoutAddressConflict) {
          final r = await showConflictDialog(context, state.conflict);
          if (!context.mounted) return;
          if (r == DriftResolution.accept) {
            context
                .read<CheckoutAddressBloc>()
                .add(AddressDriftResolved(address: _buildDto()));
          } else if (r == DriftResolution.review) {
            context.go('/checkout/${widget.sessionId}/summary');
          }
        }
      },
      builder: (context, state) {
        final submitting = state is CheckoutAddressSubmitting;
        final errors =
            state is CheckoutAddressFailure ? state.fields : const {};
        return AppScaffold(
          appBar: AppBar(title: Text(l10n.checkoutStepAddress)),
          body: Form(
            key: _formKey,
            child: ListView(
              padding: const EdgeInsets.all(AppSpacing.md),
              children: [
                _field(_name, l10n.checkoutAddressName, errors['name']),
                _field(_phone, l10n.checkoutAddressPhone, errors['phone'],
                    keyboard: TextInputType.phone),
                _field(_city, l10n.checkoutAddressCity, errors['city']),
                _field(_region, l10n.checkoutAddressRegion, errors['region']),
                _field(_street, l10n.checkoutAddressStreet, errors['street']),
                _field(_postal, l10n.checkoutAddressPostal, null),
                const SizedBox(height: AppSpacing.md),
                FilledButton(
                  onPressed: submitting
                      ? null
                      : () => context
                          .read<CheckoutAddressBloc>()
                          .add(AddressSubmitted(_buildDto())),
                  child: submitting
                      ? const CircularProgressIndicator()
                      : Text(l10n.checkoutContinue),
                ),
              ],
            ),
          ),
        );
      },
    );
  }

  Widget _field(
    TextEditingController c,
    String label,
    String? error, {
    TextInputType? keyboard,
  }) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
      child: TextField(
        controller: c,
        keyboardType: keyboard,
        decoration: InputDecoration(
          labelText: label,
          errorText: error,
          border: const OutlineInputBorder(),
        ),
      ),
    );
  }
}
