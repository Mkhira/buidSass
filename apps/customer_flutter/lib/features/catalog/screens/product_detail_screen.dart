import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../core/localization/locale_bloc.dart';
import '../../../generated/l10n/app_localizations.dart';
import '../bloc/product_detail_bloc.dart';
import '../data/catalog_view_models.dart';
import '../widgets/attribute_specs_table.dart';
import '../widgets/media_gallery.dart';
import '../widgets/price_breakdown_panel.dart';

class ProductDetailScreen extends StatelessWidget {
  const ProductDetailScreen({super.key, required this.productId});
  final String productId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Scaffold(
      appBar: AppBar(),
      body: BlocBuilder<ProductDetailBloc, ProductDetailState>(
        builder: (context, state) {
          return switch (state) {
            ProductDetailLoading() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            ProductDetailError() => ErrorState(
                title: l10n.commonErrorTitle,
                body: l10n.commonErrorBody,
                onRetry: () => context
                    .read<ProductDetailBloc>()
                    .add(ProductRequested(productId)),
                retryLabel: l10n.commonRetry,
              ),
            ProductDetailLoaded(:final product) =>
              _Body(product: product, addToCartEnabled: true),
            ProductDetailOutOfStock(:final product) =>
              _Body(product: product, addToCartEnabled: false),
            ProductDetailRestricted(:final product) => _Body(
                product: product, addToCartEnabled: false, isRestricted: true),
          };
        },
      ),
    );
  }
}

class _Body extends StatelessWidget {
  const _Body({
    required this.product,
    required this.addToCartEnabled,
    this.isRestricted = false,
  });
  final ProductDetailViewModel product;
  final bool addToCartEnabled;
  final bool isRestricted;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final localeCode = context.watch<LocaleBloc>().state.locale.code;
    return Column(
      children: [
        Expanded(
          child: ListView(
            children: [
              MediaGallery(urls: product.mediaUrls),
              Padding(
                padding: const EdgeInsets.all(AppSpacing.md),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(product.name, style: AppTypography.headline),
                    if (isRestricted)
                      Padding(
                        padding:
                            const EdgeInsets.symmetric(vertical: AppSpacing.sm),
                        child:
                            RestrictedBadge(label: l10n.verificationRequired),
                      ),
                    const SizedBox(height: AppSpacing.sm),
                    Text(product.description, style: AppTypography.body),
                  ],
                ),
              ),
              AttributeSpecsTable(attributes: product.attributes),
              PriceBreakdownPanel(
                breakdown: product.priceBreakdown,
                localeCode: localeCode,
              ),
            ],
          ),
        ),
        SafeArea(
          minimum: const EdgeInsets.all(AppSpacing.md),
          child: AppButton(
            label: l10n.cartProceedToCheckout,
            expand: true,
            onPressed: addToCartEnabled ? () => context.go('/cart') : null,
          ),
        ),
      ],
    );
  }
}
