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
    this.copy = const FilterBarCopy(),
  });

  final CatalogSort sort;
  final ValueChanged<CatalogSort> onSortChanged;
  final String locale;
  final List<CatalogBrand> brands;
  final String? selectedBrandSlug;
  final ValueChanged<String?>? onBrandChanged;

  /// All user-facing copy (field labels + sort enum labels). Defaults
  /// are English placeholders; screen layer passes locale-resolved copy.
  final FilterBarCopy copy;

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
                decoration: InputDecoration(
                  isDense: true,
                  border: const OutlineInputBorder(),
                  labelText: copy.brandLabel,
                ),
                items: [
                  DropdownMenuItem(value: null, child: Text(copy.allBrands)),
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
              decoration: InputDecoration(
                isDense: true,
                border: const OutlineInputBorder(),
                labelText: copy.sortLabel,
              ),
              items: [
                for (final s in CatalogSort.values)
                  DropdownMenuItem(value: s, child: Text(copy.labelFor(s))),
              ],
              onChanged: (v) => v == null ? null : onSortChanged(v),
            ),
          ),
        ],
      ),
    );
  }
}

/// Locale-resolved copy for [FilterBar]. Defaults are English
/// placeholders; the screen layer passes localized strings from the i18n
/// catalog.
class FilterBarCopy {
  const FilterBarCopy({
    this.brandLabel = 'Brand',
    this.sortLabel = 'Sort',
    this.allBrands = 'All',
    this.relevance = 'Relevance',
    this.priceAsc = 'Price low to high',
    this.priceDesc = 'Price high to low',
    this.newest = 'Newest',
  });

  final String brandLabel;
  final String sortLabel;
  final String allBrands;
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
