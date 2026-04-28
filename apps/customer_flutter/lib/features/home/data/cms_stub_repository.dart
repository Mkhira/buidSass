/// CmsStubRepository — static-fixture stand-in for the spec 022 CMS adapter.
/// Implements the same shape that the real adapter will satisfy so the home
/// Bloc swaps over with a single DI binding when 022 ships.
class CmsHomeBanner {
  const CmsHomeBanner({
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

class CmsFeaturedSection {
  const CmsFeaturedSection({
    required this.id,
    required this.titleKey,
    required this.productIds,
  });

  final String id;
  final String titleKey;
  final List<String> productIds;
}

class CmsCategoryTile {
  const CmsCategoryTile({
    required this.categoryId,
    required this.order,
  });

  final String categoryId;
  final int order;
}

class CmsHomePayload {
  const CmsHomePayload({
    required this.banners,
    required this.featured,
    required this.categoryTiles,
  });

  final List<CmsHomeBanner> banners;
  final List<CmsFeaturedSection> featured;
  final List<CmsCategoryTile> categoryTiles;
}

abstract class CmsRepository {
  Future<CmsHomePayload> fetchHome();
}

class CmsStubRepository implements CmsRepository {
  const CmsStubRepository();

  @override
  Future<CmsHomePayload> fetchHome() async {
    return const CmsHomePayload(
      banners: [
        CmsHomeBanner(
          id: 'banner-welcome',
          titleKey: 'home.banner.welcome',
          imageUrl: '',
          deeplink: '/c/featured',
        ),
      ],
      featured: [
        CmsFeaturedSection(
          id: 'featured-essentials',
          titleKey: 'home.featured.essentials',
          productIds: <String>[],
        ),
      ],
      categoryTiles: [
        CmsCategoryTile(categoryId: 'instruments', order: 0),
        CmsCategoryTile(categoryId: 'consumables', order: 1),
        CmsCategoryTile(categoryId: 'lab', order: 2),
        CmsCategoryTile(categoryId: 'oral_care', order: 3),
      ],
    );
  }
}
