import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

import '../bloc/listing_bloc.dart';
import '../data/catalog_view_models.dart';

class FacetDrawer extends StatelessWidget {
  const FacetDrawer({
    super.key,
    required this.facets,
    required this.filter,
    required this.onToggle,
  });

  final List<Facet> facets;
  final ListingFilter filter;
  final void Function(String kind, String value) onToggle;

  @override
  Widget build(BuildContext context) {
    return Drawer(
      child: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(AppSpacing.md),
          children: facets.map((f) {
            final selected = filter.selectedFacets[f.kind] ?? const <String>{};
            return Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(f.kind, style: AppTypography.headline),
                const SizedBox(height: AppSpacing.xs),
                ...f.options.map((o) => CheckboxListTile(
                      title: Text(o.labelKey),
                      subtitle: Text('${o.count}'),
                      value: selected.contains(o.value),
                      onChanged: (_) => onToggle(f.kind, o.value),
                    )),
                const SizedBox(height: AppSpacing.md),
              ],
            );
          }).toList(growable: false),
        ),
      ),
    );
  }
}
