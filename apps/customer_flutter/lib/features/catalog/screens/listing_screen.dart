import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/listing_bloc.dart';
import '../widgets/facet_drawer.dart';
import '../widgets/product_grid_tile.dart';
import '../widgets/sort_menu.dart';

class ListingScreen extends StatefulWidget {
  const ListingScreen({super.key});

  @override
  State<ListingScreen> createState() => _ListingScreenState();
}

class _ListingScreenState extends State<ListingScreen> {
  final _scrollController = ScrollController();
  final _queryController = TextEditingController();

  @override
  void initState() {
    super.initState();
    _scrollController.addListener(_onScroll);
  }

  void _onScroll() {
    if (_scrollController.position.pixels >=
        _scrollController.position.maxScrollExtent - 200) {
      context.read<ListingBloc>().add(const PageRequested());
    }
  }

  @override
  void dispose() {
    _scrollController.dispose();
    _queryController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return BlocBuilder<ListingBloc, ListingState>(
      builder: (context, state) {
        final facets = state is ListingLoaded ? state.facets : const [];
        return Scaffold(
          endDrawer: FacetDrawer(
            facets: List.castFrom(facets),
            filter: state.filter,
            onToggle: (k, v) => context
                .read<ListingBloc>()
                .add(FacetToggled(kind: k, value: v)),
          ),
          appBar: AppBar(
            title: TextField(
              controller: _queryController,
              onChanged: (v) =>
                  context.read<ListingBloc>().add(QueryChanged(v)),
              decoration: InputDecoration(
                hintText: l10n.navCatalog,
                border: InputBorder.none,
              ),
            ),
            actions: [
              SortMenu(
                activeKey: state.filter.sortKey,
                options: const {
                  'relevance': 'relevance',
                  'priceAsc': 'price ↑',
                  'priceDesc': 'price ↓',
                },
                onSelected: (k) =>
                    context.read<ListingBloc>().add(SortChanged(k)),
              ),
              Builder(
                builder: (ctx) => IconButton(
                  icon: const Icon(Icons.tune),
                  onPressed: () => Scaffold.of(ctx).openEndDrawer(),
                ),
              ),
            ],
          ),
          body: switch (state) {
            ListingIdle() ||
            ListingLoading() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            ListingEmpty() => EmptyState(title: l10n.commonEmpty),
            ListingError() => ErrorState(
                title: l10n.commonErrorTitle,
                body: l10n.commonErrorBody,
                onRetry: () =>
                    context.read<ListingBloc>().add(const QueryChanged('')),
                retryLabel: l10n.commonRetry,
              ),
            ListingLoaded(:final items, :final isLoadingMore) =>
              GridView.builder(
                controller: _scrollController,
                padding: const EdgeInsets.all(AppSpacing.md),
                gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
                  crossAxisCount: 2,
                  mainAxisSpacing: AppSpacing.md,
                  crossAxisSpacing: AppSpacing.md,
                  childAspectRatio: 0.7,
                ),
                itemCount: items.length + (isLoadingMore ? 1 : 0),
                itemBuilder: (ctx, i) {
                  if (i == items.length) {
                    return const Center(child: CircularProgressIndicator());
                  }
                  final item = items[i];
                  return ProductGridTile(
                    item: item,
                    onTap: () => context.go('/p/${item.id}'),
                  );
                },
              ),
          },
        );
      },
    );
  }
}
