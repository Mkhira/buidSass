import 'package:design_system/design_system.dart';
import 'package:flutter/material.dart';

import '../data/models/catalog_models.dart';

/// Compact filter + sort bar over a product list. Sort options come from
/// the server-supplied [CatalogSort] enum (BR-9 — mobile never invents
/// sort keys). Brand chip is optional; when [brands] is empty the bar
/// renders only the sort dropdown.
class FilterBar extends StatelessWidget {
  const FilterBar({
    super.key,
    required this.sort,
    required this.onSortChanged,
    required this.locale,
    this.brands = const [],
    this.selectedBrandSlug,
    this.onBrandChanged,
    this.sortLabels = const FilterBarSortLabels(),
  });

  final CatalogSort sort;
  final ValueChanged<CatalogSort> onSortChanged;
  final String locale;
  final List<CatalogBrand> brands;
  final String? selectedBrandSlug;
  final ValueChanged<String?>? onBrandChanged;
  final FilterBarSortLabels sortLabels;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.md,
        vertical: AppSpacing.sm,
      ),
      child: Row(
        children: [
          if (brands.isNotEmpty && onBrandChanged != null) ...[
            Expanded(
              child: DropdownButtonFormField<String?>(
                initialValue: selectedBrandSlug,
                decoration: const InputDecoration(
                  isDense: true,
                  border: OutlineInputBorder(),
                  labelText: 'Brand',
                ),
                items: [
                  const DropdownMenuItem(value: null, child: Text('All')),
                  for (final b in brands)
                    DropdownMenuItem(
                      value: b.slug,
                      child: Text(b.name.resolve(locale)),
                    ),
                ],
                onChanged: onBrandChanged,
              ),
            ),
            const SizedBox(width: AppSpacing.sm),
          ],
          Expanded(
            child: DropdownButtonFormField<CatalogSort>(
              initialValue: sort,
              decoration: const InputDecoration(
                isDense: true,
                border: OutlineInputBorder(),
                labelText: 'Sort',
              ),
              items: [
                for (final s in CatalogSort.values)
                  DropdownMenuItem(value: s, child: Text(sortLabels.labelFor(s))),
              ],
              onChanged: (v) => v == null ? null : onSortChanged(v),
            ),
          ),
        ],
      ),
    );
  }
}

class FilterBarSortLabels {
  const FilterBarSortLabels({
    this.relevance = 'Relevance',
    this.priceAsc = 'Price low to high',
    this.priceDesc = 'Price high to low',
    this.newest = 'Newest',
  });

  final String relevance;
  final String priceAsc;
  final String priceDesc;
  final String newest;

  String labelFor(CatalogSort s) => switch (s) {
        CatalogSort.relevance => relevance,
        CatalogSort.priceAsc => priceAsc,
        CatalogSort.priceDesc => priceDesc,
        CatalogSort.newest => newest,
      };
}
