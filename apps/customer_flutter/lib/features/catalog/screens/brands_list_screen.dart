import 'package:design_system/design_system.dart';
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../bloc/brands_list_bloc.dart';
import '../data/models/catalog_models.dart';

/// S-2.4 — brands list. Two-column grid of brand tiles with logos
/// (initials fallback when [logoUrl] is missing). Tap routes to
/// `/brands/{slug}/products` (handled by ProductListScreen with brand
/// filter).
class BrandsListScreen extends StatelessWidget {
  const BrandsListScreen({
    super.key,
    required this.locale,
    required this.onBrandTap,
    this.title = 'Brands',
  });

  final String locale;
  final ValueChanged<CatalogBrand> onBrandTap;
  final String title;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(title)),
      body: BlocBuilder<BrandsListBloc, BrandsListState>(
        builder: (context, state) {
          return switch (state) {
            BrandsListLoading() => const Center(
                child: CircularProgressIndicator(),
              ),
            BrandsListEmpty() => const Center(child: Text('No brands')),
            BrandsListError(:final failure) => Center(
                child: Padding(
                  padding: const EdgeInsets.all(AppSpacing.md),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      const Icon(Icons.error_outline,
                          size: 32, color: AppColors.danger),
                      const SizedBox(height: AppSpacing.sm),
                      Text('Failed to load. (${failure.correlationIdShort})'),
                      const SizedBox(height: AppSpacing.sm),
                      FilledButton(
                        onPressed: () => context
                            .read<BrandsListBloc>()
                            .add(const BrandsListRequested()),
                        child: const Text('Retry'),
                      ),
                    ],
                  ),
                ),
              ),
            BrandsListLoaded(:final brands) => RefreshIndicator(
                onRefresh: () async => context
                    .read<BrandsListBloc>()
                    .add(const BrandsListRequested()),
                child: GridView.builder(
                  padding: const EdgeInsets.all(AppSpacing.md),
                  itemCount: brands.length,
                  gridDelegate:
                      const SliverGridDelegateWithFixedCrossAxisCount(
                    crossAxisCount: 2,
                    mainAxisSpacing: AppSpacing.sm,
                    crossAxisSpacing: AppSpacing.sm,
                    childAspectRatio: 1.6,
                  ),
                  itemBuilder: (_, i) {
                    final b = brands[i];
                    return _BrandTile(
                      brand: b,
                      locale: locale,
                      onTap: () => onBrandTap(b),
                    );
                  },
                ),
              ),
          };
        },
      ),
    );
  }
}

class _BrandTile extends StatelessWidget {
  const _BrandTile({
    required this.brand,
    required this.locale,
    required this.onTap,
  });

  final CatalogBrand brand;
  final String locale;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final name = brand.name.resolve(locale);
    return Card(
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.sm),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Expanded(
                child: brand.logoUrl != null && brand.logoUrl!.isNotEmpty
                    ? Image.network(
                        brand.logoUrl!,
                        fit: BoxFit.contain,
                        errorBuilder: (_, __, ___) => _Initials(name: name),
                      )
                    : _Initials(name: name),
              ),
              const SizedBox(height: AppSpacing.xs),
              Text(
                name,
                textAlign: TextAlign.center,
                style: const TextStyle(fontWeight: FontWeight.w600),
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _Initials extends StatelessWidget {
  const _Initials({required this.name});
  final String name;

  @override
  Widget build(BuildContext context) {
    final parts = name.trim().split(RegExp(r'\s+'));
    final initials = parts.take(2).map((p) => p.isEmpty ? '' : p[0]).join();
    return Container(
      decoration: BoxDecoration(
        color: AppColors.accent.withValues(alpha: 0.2),
        shape: BoxShape.circle,
      ),
      alignment: Alignment.center,
      child: Text(
        initials.toUpperCase(),
        style: const TextStyle(
          fontSize: 18,
          fontWeight: FontWeight.w700,
          color: AppColors.primary,
        ),
      ),
    );
  }
}
