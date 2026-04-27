import 'package:flutter/foundation.dart';

@immutable
class CategoryTileViewModel {
  const CategoryTileViewModel({
    required this.id,
    required this.labelKey,
    this.iconUrl,
  });
  final String id;
  final String labelKey;
  final String? iconUrl;
}

@immutable
class FeaturedProductViewModel {
  const FeaturedProductViewModel({
    required this.id,
    required this.name,
    required this.thumbnailUrl,
    required this.priceMinor,
    required this.currency,
    required this.isRestricted,
  });
  final String id;
  final String name;
  final String thumbnailUrl;
  final int priceMinor;
  final String currency;
  final bool isRestricted;
}

@immutable
class HomeBannerViewModel {
  const HomeBannerViewModel({
    required this.id,
    required this.titleKey,
    required this.imageUrl,
    required this.deeplink,
  });
  final String id;
  final String titleKey;
  final String imageUrl;
  final String deeplink;
}

@immutable
class HomePayloadViewModel {
  const HomePayloadViewModel({
    required this.banners,
    required this.featured,
    required this.categories,
  });
  final List<HomeBannerViewModel> banners;
  final List<FeaturedProductViewModel> featured;
  final List<CategoryTileViewModel> categories;

  bool get isEmpty =>
      banners.isEmpty && featured.isEmpty && categories.isEmpty;
}
