import '../../../core/api/i18n_aware_repository.dart';
import 'cms_stub_repository.dart';
import 'home_view_models.dart';

abstract class HomeRepository {
  Future<HomePayloadViewModel> fetchHome();
}

/// Default repository — composes CMS payload (real or stub) with category +
/// featured-product hydration. While spec 005's catalog client is unavailable
/// the featured list comes back empty and the screen renders the
/// 'no featured items yet' empty state.
class DefaultHomeRepository with I18nAwareRepository implements HomeRepository {
  DefaultHomeRepository({required this.cms});

  final CmsRepository cms;

  @override
  Future<HomePayloadViewModel> fetchHome() async {
    final payload = await cms.fetchHome();
    return HomePayloadViewModel(
      banners: payload.banners
          .map((b) => HomeBannerViewModel(
                id: b.id,
                titleKey: b.titleKey,
                imageUrl: b.imageUrl,
                deeplink: b.deeplink,
              ))
          .toList(growable: false),
      featured: const <FeaturedProductViewModel>[],
      categories: payload.categoryTiles
          .map((c) => CategoryTileViewModel(
                id: c.categoryId,
                labelKey: 'category.${c.categoryId}',
              ))
          .toList(growable: false),
    );
  }
}
