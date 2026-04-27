import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/market/market_resolver.dart';
import '../../../generated/l10n/app_localizations.dart';
import '../bloc/addresses_bloc.dart';
import '../widgets/address_form.dart';

class AddressesScreen extends StatelessWidget {
  const AddressesScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final market = context.read<MarketResolver>().resolve().code;
    return Scaffold(
      appBar: AppBar(title: Text(l10n.commonContinue)),
      body: BlocBuilder<AddressesBloc, AddressesState>(
        builder: (context, state) {
          return switch (state) {
            AddressesLoading() => LoadingState(semanticsLabel: l10n.commonLoading),
            AddressesEmpty() => Padding(
                padding: const EdgeInsets.all(AppSpacing.md),
                child: AddressForm(
                  marketCode: market,
                  onSubmit: (d) =>
                      context.read<AddressesBloc>().add(AddressCreated(d)),
                ),
              ),
            AddressesError() => ErrorState(
                title: l10n.commonErrorTitle,
                body: l10n.commonErrorBody,
                onRetry: () => context
                    .read<AddressesBloc>()
                    .add(const AddressesRequested()),
                retryLabel: l10n.commonRetry,
              ),
            AddressesLoaded(:final addresses) => ListView(
                children: [
                  ...addresses.map((a) => AppListTile(
                        title: a.label,
                        subtitle: '${a.line1}, ${a.city}',
                        trailing: a.isDefault
                            ? const Icon(Icons.star, color: AppColors.warning)
                            : IconButton(
                                icon: const Icon(Icons.star_border),
                                onPressed: () => context
                                    .read<AddressesBloc>()
                                    .add(AddressMadeDefault(a.id)),
                              ),
                      )),
                  const Divider(),
                  Padding(
                    padding: const EdgeInsets.all(AppSpacing.md),
                    child: AddressForm(
                      marketCode: market,
                      onSubmit: (d) => context
                          .read<AddressesBloc>()
                          .add(AddressCreated(d)),
                    ),
                  ),
                ],
              ),
          };
        },
      ),
    );
  }
}
