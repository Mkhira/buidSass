import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/search_bloc.dart';
import '../data/models/search_models.dart';

/// Customer search screen — covers S-3.1 entry, S-3.2 autocomplete, and
/// S-3.3 results in one widget. Sub-state rendering branches off the
/// active [SearchState] (plan.md §Bloc structure).
class SearchScreen extends StatefulWidget {
  const SearchScreen({super.key, this.initialQuery});

  final String? initialQuery;

  @override
  State<SearchScreen> createState() => _SearchScreenState();
}

class _SearchScreenState extends State<SearchScreen> {
  late final TextEditingController _controller;
  late final FocusNode _focus;

  @override
  void initState() {
    super.initState();
    _controller = TextEditingController(text: widget.initialQuery ?? '');
    _focus = FocusNode();
    // Focus the input on entry (S-3.1 AC).
    WidgetsBinding.instance.addPostFrameCallback((_) {
      _focus.requestFocus();
      context.read<SearchBloc>().add(const SearchEntered());
      final q = widget.initialQuery?.trim() ?? '';
      if (q.isNotEmpty) {
        context.read<SearchBloc>().add(SearchSubmitted(q));
      }
    });
  }

  @override
  void dispose() {
    _controller.dispose();
    _focus.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(
        title: _SearchInput(
          controller: _controller,
          focus: _focus,
          hint: l10n.searchHint,
        ),
        leading: Navigator.of(context).canPop()
            ? IconButton(
                icon: const Icon(Icons.arrow_back),
                onPressed: () => Navigator.of(context).pop(),
                tooltip: l10n.commonClose,
              )
            : null,
      ),
      body: BlocBuilder<SearchBloc, SearchState>(
        builder: (context, state) {
          return switch (state) {
            SearchIdle() => _IdleBody(state: state),
            SearchAutocompleting() => const Center(
                child: Padding(
                  padding: EdgeInsets.all(AppSpacing.lg),
                  child: CircularProgressIndicator(),
                ),
              ),
            SearchAutocompleted() => _AutocompletedBody(state: state),
            SearchResults() => _ResultsBody(state: state),
            SearchEmpty(:final query, :final suggestions) => _EmptyBody(
                query: query,
                suggestions: suggestions,
              ),
            SearchFailure(:final reason, :final correlationId) => ErrorState(
                title: l10n.commonErrorTitle,
                body: '$reason${correlationId == null ? '' : ' · $correlationId'}',
                onRetry: () {
                  final q = _controller.text.trim();
                  if (q.isNotEmpty) {
                    context.read<SearchBloc>().add(SearchSubmitted(q));
                  }
                },
                retryLabel: l10n.commonRetry,
              ),
          };
        },
      ),
    );
  }
}

class _SearchInput extends StatelessWidget {
  const _SearchInput({
    required this.controller,
    required this.focus,
    required this.hint,
  });

  final TextEditingController controller;
  final FocusNode focus;
  final String hint;

  @override
  Widget build(BuildContext context) {
    return TextField(
      controller: controller,
      focusNode: focus,
      autofocus: true,
      textInputAction: TextInputAction.search,
      decoration: InputDecoration(
        hintText: hint,
        border: InputBorder.none,
        suffixIcon: ValueListenableBuilder<TextEditingValue>(
          valueListenable: controller,
          builder: (context, value, _) {
            if (value.text.isEmpty) return const SizedBox.shrink();
            return IconButton(
              icon: const Icon(Icons.close),
              onPressed: () {
                controller.clear();
                context.read<SearchBloc>().add(const SearchQueryChanged(''));
              },
            );
          },
        ),
      ),
      onChanged: (v) =>
          context.read<SearchBloc>().add(SearchQueryChanged(v)),
      onSubmitted: (v) {
        final t = v.trim();
        if (t.isEmpty) return;
        context.read<SearchBloc>().add(SearchSubmitted(t));
      },
    );
  }
}

class _IdleBody extends StatelessWidget {
  const _IdleBody({required this.state});
  final SearchIdle state;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final hasRecent = state.recent.isNotEmpty;
    if (!hasRecent && state.popular.isEmpty) {
      return Center(
        child: TextButton.icon(
          icon: const Icon(Icons.qr_code_scanner),
          label: Text(l10n.searchLookupCta),
          onPressed: () => context.push('/search/lookup'),
        ),
      );
    }
    return ListView(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.md),
      children: [
        if (hasRecent) ...[
          Padding(
            padding: const EdgeInsets.symmetric(
                horizontal: AppSpacing.lg, vertical: AppSpacing.sm),
            child: Row(
              children: [
                Text(l10n.searchRecentTitle,
                    style: Theme.of(context).textTheme.titleSmall),
                const Spacer(),
                TextButton(
                  onPressed: () => _confirmClear(context),
                  child: Text(l10n.searchRecentClear),
                ),
              ],
            ),
          ),
          ...state.recent.map(
            (q) => ListTile(
              key: ValueKey('search-recent-$q'),
              leading: const Icon(Icons.history),
              title: Text(q),
              onTap: () =>
                  context.read<SearchBloc>().add(SearchRecentTapped(q)),
            ),
          ),
        ],
        const SizedBox(height: AppSpacing.md),
        ListTile(
          leading: const Icon(Icons.qr_code_scanner),
          title: Text(l10n.searchLookupCta),
          onTap: () => context.push('/search/lookup'),
        ),
      ],
    );
  }

  Future<void> _confirmClear(BuildContext context) async {
    final l10n = AppLocalizations.of(context);
    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text(l10n.searchRecentClearConfirm),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(ctx).pop(false),
            child: Text(l10n.commonCancel),
          ),
          TextButton(
            onPressed: () => Navigator.of(ctx).pop(true),
            child: Text(l10n.commonOk),
          ),
        ],
      ),
    );
    if (ok == true && context.mounted) {
      context.read<SearchBloc>().add(const SearchRecentCleared());
    }
  }
}

class _AutocompletedBody extends StatelessWidget {
  const _AutocompletedBody({required this.state});
  final SearchAutocompleted state;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final hasAny =
        state.suggestions.isNotEmpty || state.topMatches.isNotEmpty;
    if (!hasAny) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.lg),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(l10n.searchEmptyTitle,
                  style: Theme.of(context).textTheme.titleMedium),
              const SizedBox(height: AppSpacing.sm),
              TextButton(
                onPressed: () => context.push('/search/lookup'),
                child: Text(l10n.searchLookupCta),
              ),
            ],
          ),
        ),
      );
    }
    return ListView(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.sm),
      children: [
        if (state.topMatches.isNotEmpty) ...[
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: AppSpacing.lg),
            child: Text(l10n.searchTopMatchesTitle,
                style: Theme.of(context).textTheme.titleSmall),
          ),
          SizedBox(
            height: 140,
            child: ListView.separated(
              padding: const EdgeInsets.symmetric(
                  horizontal: AppSpacing.lg, vertical: AppSpacing.sm),
              scrollDirection: Axis.horizontal,
              itemCount: state.topMatches.length,
              separatorBuilder: (_, __) =>
                  const SizedBox(width: AppSpacing.sm),
              itemBuilder: (context, i) {
                final m = state.topMatches[i];
                return _TopMatchTile(match: m);
              },
            ),
          ),
        ],
        if (state.suggestions.isNotEmpty) ...[
          Padding(
            padding: const EdgeInsets.symmetric(
                horizontal: AppSpacing.lg, vertical: AppSpacing.xs),
            child: Text(l10n.searchSuggestionsTitle,
                style: Theme.of(context).textTheme.titleSmall),
          ),
          ...state.suggestions.map(
            (s) => ListTile(
              leading: Icon(_suggestionIcon(s.kind)),
              title: Text(s.label),
              onTap: () =>
                  context.read<SearchBloc>().add(SearchSubmitted(s.label)),
            ),
          ),
        ],
      ],
    );
  }

  IconData _suggestionIcon(String kind) {
    return switch (kind) {
      'category' => Icons.category_outlined,
      'brand' => Icons.label_outline,
      _ => Icons.search,
    };
  }
}

class _TopMatchTile extends StatelessWidget {
  const _TopMatchTile({required this.match});
  final SearchTopMatch match;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: () => context.push('/p/${match.slug}'),
      child: SizedBox(
        width: 120,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            AspectRatio(
              aspectRatio: 1,
              child: ColoredBox(
                color: AppColors.neutral,
                child: match.imageUrl.isEmpty
                    ? const Icon(Icons.image_outlined)
                    : Image.network(match.imageUrl, fit: BoxFit.cover),
              ),
            ),
            const SizedBox(height: AppSpacing.xs),
            Text(
              match.name,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: Theme.of(context).textTheme.bodyMedium,
            ),
            Text(
              '${match.priceHint.amount} ${match.priceHint.currency}',
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ],
        ),
      ),
    );
  }
}

class _ResultsBody extends StatefulWidget {
  const _ResultsBody({required this.state});
  final SearchResults state;

  @override
  State<_ResultsBody> createState() => _ResultsBodyState();
}

class _ResultsBodyState extends State<_ResultsBody> {
  final ScrollController _scroll = ScrollController();

  @override
  void initState() {
    super.initState();
    _scroll.addListener(_onScroll);
  }

  @override
  void dispose() {
    _scroll.removeListener(_onScroll);
    _scroll.dispose();
    super.dispose();
  }

  void _onScroll() {
    if (_scroll.position.pixels >=
        _scroll.position.maxScrollExtent - 200) {
      context.read<SearchBloc>().add(const SearchPageRequested());
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final s = widget.state;
    return Column(
      children: [
        _ResultsToolbar(state: s),
        Expanded(
          child: GridView.builder(
            controller: _scroll,
            padding: const EdgeInsets.all(AppSpacing.md),
            gridDelegate:
                const SliverGridDelegateWithFixedCrossAxisCount(
              crossAxisCount: 2,
              mainAxisExtent: 240,
              crossAxisSpacing: AppSpacing.sm,
              mainAxisSpacing: AppSpacing.sm,
            ),
            itemCount: s.items.length + (s.isLoadingMore ? 1 : 0),
            itemBuilder: (context, i) {
              if (i >= s.items.length) {
                return const Center(child: CircularProgressIndicator());
              }
              return _SearchResultCard(item: s.items[i]);
            },
          ),
        ),
        if (s.items.isEmpty)
          Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: Text(l10n.searchResultsCount(0)),
          ),
      ],
    );
  }
}

class _ResultsToolbar extends StatelessWidget {
  const _ResultsToolbar({required this.state});
  final SearchResults state;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(
          horizontal: AppSpacing.md, vertical: AppSpacing.xs),
      child: Row(
        children: [
          Expanded(
            child: Text(l10n.searchResultsCount(state.totalCount),
                style: Theme.of(context).textTheme.bodySmall),
          ),
          if (state.facets.isNotEmpty)
            TextButton.icon(
              icon: const Icon(Icons.filter_list),
              label: Text(l10n.searchFiltersLabel),
              onPressed: () => _showFacets(context),
            ),
          if (state.sortOptions.isNotEmpty)
            PopupMenuButton<String>(
              tooltip: l10n.searchSortLabel,
              icon: const Icon(Icons.sort),
              itemBuilder: (ctx) => [
                for (final s in state.sortOptions)
                  PopupMenuItem<String>(
                    value: s.key,
                    child: Text(s.label),
                  ),
              ],
              onSelected: (key) =>
                  context.read<SearchBloc>().add(SearchSortChanged(key)),
            ),
        ],
      ),
    );
  }

  Future<void> _showFacets(BuildContext context) async {
    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      builder: (ctx) {
        return SafeArea(
          child: ListView(
            shrinkWrap: true,
            children: [
              for (final facet in state.facets) ...[
                Padding(
                  padding: const EdgeInsets.all(AppSpacing.md),
                  child: Text(facet.label,
                      style: Theme.of(ctx).textTheme.titleSmall),
                ),
                Wrap(
                  spacing: AppSpacing.xs,
                  runSpacing: AppSpacing.xs,
                  children: [
                    for (final opt in facet.options)
                      Padding(
                        padding: const EdgeInsets.symmetric(
                            horizontal: AppSpacing.md),
                        child: FilterChip(
                          label: Text('${opt.label} (${opt.count})'),
                          selected: state.selectedFacets[facet.key]
                                  ?.contains(opt.value) ??
                              false,
                          onSelected: (_) {
                            context.read<SearchBloc>().add(
                                  SearchFacetToggled(
                                      kind: facet.key, value: opt.value),
                                );
                            Navigator.of(ctx).pop();
                          },
                        ),
                      ),
                  ],
                ),
              ],
            ],
          ),
        );
      },
    );
  }
}

class _SearchResultCard extends StatelessWidget {
  const _SearchResultCard({required this.item});
  final SearchProductItem item;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final priceMajor = (item.priceMinor / 100).toStringAsFixed(2);
    return Card(
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: () => context.push('/p/${item.slug}'),
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.sm),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: AspectRatio(
                  aspectRatio: 1,
                  child: ColoredBox(
                    color: AppColors.neutral,
                    child: item.thumbnailUrl.isEmpty
                        ? const Icon(Icons.image_outlined)
                        : Image.network(item.thumbnailUrl, fit: BoxFit.cover),
                  ),
                ),
              ),
              const SizedBox(height: AppSpacing.xs),
              Text(
                item.name,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: Theme.of(context).textTheme.bodyMedium,
              ),
              const SizedBox(height: AppSpacing.xs),
              Row(
                children: [
                  Expanded(
                    child: Text(
                      '$priceMajor ${item.currency}',
                      style: Theme.of(context).textTheme.titleSmall,
                    ),
                  ),
                  if (!item.inStock)
                    Tooltip(
                      message: l10n.stockOutOfStock,
                      child: const Icon(Icons.do_not_disturb,
                          size: 16, color: Colors.red),
                    ),
                  if (item.isRestricted)
                    Tooltip(
                      message: l10n.verificationRequired,
                      child: const Icon(Icons.verified_user_outlined,
                          size: 16),
                    ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _EmptyBody extends StatelessWidget {
  const _EmptyBody({required this.query, required this.suggestions});
  final String query;
  final List<String> suggestions;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return ListView(
      padding: const EdgeInsets.all(AppSpacing.lg),
      children: [
        const SizedBox(height: AppSpacing.lg),
        Icon(Icons.search_off,
            size: 64, color: Theme.of(context).colorScheme.outline),
        const SizedBox(height: AppSpacing.md),
        Text(l10n.searchEmptyTitle,
            textAlign: TextAlign.center,
            style: Theme.of(context).textTheme.titleMedium),
        const SizedBox(height: AppSpacing.sm),
        Text(l10n.searchEmptyBody, textAlign: TextAlign.center),
        const SizedBox(height: AppSpacing.lg),
        if (suggestions.isNotEmpty) ...[
          Text(l10n.searchSuggestionsTitle,
              style: Theme.of(context).textTheme.titleSmall),
          const SizedBox(height: AppSpacing.sm),
          Wrap(
            spacing: AppSpacing.xs,
            children: [
              for (final s in suggestions)
                ActionChip(
                  label: Text(s),
                  onPressed: () =>
                      context.read<SearchBloc>().add(SearchSubmitted(s)),
                ),
            ],
          ),
          const SizedBox(height: AppSpacing.lg),
        ],
        Center(
          child: TextButton.icon(
            icon: const Icon(Icons.qr_code_scanner),
            label: Text(l10n.searchLookupCta),
            onPressed: () => context.push('/search/lookup'),
          ),
        ),
      ],
    );
  }
}
