import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../data/address_view_models.dart';
import '../services/address_validators.dart';

class AddressForm extends StatefulWidget {
  const AddressForm({
    super.key,
    required this.marketCode,
    required this.onSubmit,
    this.initial,
  });

  final String marketCode;
  final ValueChanged<AddressDraft> onSubmit;
  final AddressViewModel? initial;

  @override
  State<AddressForm> createState() => _AddressFormState();
}

class _AddressFormState extends State<AddressForm> {
  final _formKey = GlobalKey<FormState>();
  late final _label = TextEditingController(text: widget.initial?.label);
  late final _recipient = TextEditingController(text: widget.initial?.recipient);
  late final _line1 = TextEditingController(text: widget.initial?.line1);
  late final _city = TextEditingController(text: widget.initial?.city);
  late final _country = TextEditingController(text: widget.initial?.country);
  late final _postal = TextEditingController(text: widget.initial?.postalCode);
  late final _phone = TextEditingController(text: widget.initial?.phone);

  @override
  void dispose() {
    _label.dispose();
    _recipient.dispose();
    _line1.dispose();
    _city.dispose();
    _country.dispose();
    _postal.dispose();
    _phone.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Form(
      key: _formKey,
      child: Column(
        children: [
          AppTextField(label: l10n.commonSave, controller: _label),
          const SizedBox(height: AppSpacing.sm),
          AppTextField(label: l10n.commonSave, controller: _recipient),
          const SizedBox(height: AppSpacing.sm),
          AppTextField(label: l10n.commonSave, controller: _line1),
          const SizedBox(height: AppSpacing.sm),
          AppTextField(label: l10n.commonSave, controller: _city),
          const SizedBox(height: AppSpacing.sm),
          AppTextField(label: l10n.commonSave, controller: _country),
          const SizedBox(height: AppSpacing.sm),
          _ValidatedField(
            controller: _postal,
            label: l10n.commonSave,
            keyboardType: TextInputType.number,
            validator: (v) => AddressValidators.validatePostalCodeOrNull(
              marketCode: widget.marketCode,
              value: v,
            ),
          ),
          const SizedBox(height: AppSpacing.sm),
          _ValidatedField(
            controller: _phone,
            label: l10n.commonSave,
            keyboardType: TextInputType.phone,
            validator: AddressValidators.validatePhoneOrNull,
          ),
          const SizedBox(height: AppSpacing.lg),
          AppButton(
            label: l10n.commonSave,
            expand: true,
            onPressed: () {
              if (_formKey.currentState?.validate() != true) return;
              widget.onSubmit(AddressDraft(
                label: _label.text.trim(),
                recipient: _recipient.text.trim(),
                line1: _line1.text.trim(),
                city: _city.text.trim(),
                country: _country.text.trim(),
                postalCode: _postal.text.trim(),
                marketCode: widget.marketCode,
                phone: _phone.text.trim(),
                isDefault: widget.initial?.isDefault ?? false,
              ));
            },
          ),
        ],
      ),
    );
  }
}

class _ValidatedField extends StatelessWidget {
  const _ValidatedField({
    required this.controller,
    required this.label,
    required this.validator,
    this.keyboardType,
  });

  final TextEditingController controller;
  final String label;
  final FormFieldValidator<String> validator;
  final TextInputType? keyboardType;

  @override
  Widget build(BuildContext context) {
    return TextFormField(
      controller: controller,
      keyboardType: keyboardType,
      decoration: InputDecoration(labelText: label),
      validator: validator,
    );
  }
}
