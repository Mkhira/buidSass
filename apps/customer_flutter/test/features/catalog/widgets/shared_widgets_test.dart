import 'package:customer_flutter/features/catalog/data/models/catalog_models.dart';
import 'package:customer_flutter/features/catalog/widgets/filter_bar.dart';
import 'package:customer_flutter/features/catalog/widgets/price_label.dart';
import 'package:customer_flutter/features/catalog/widgets/product_card.dart';
import 'package:customer_flutter/features/catalog/widgets/rating_block.dart';
import 'package:customer_flutter/features/catalog/widgets/restriction_gate.dart';
import 'package:customer_flutter/features/catalog/widgets/stock_badge.dart';
import 'package:customer_flutter/features/inventory/data/models/inventory_models.dart';
import 'package:customer_flutter/features/reviews/data/models/reviews_aggregate_models.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

Widget _wrap(Widget child, {Locale locale = const Locale('en')}) {
  return MaterialApp(
    locale: locale,
    supportedLocales: const [Locale('en'), Locale('ar')],
    localizationsDelegates: const [
      GlobalMaterialLocalizations.delegate,
      GlobalWidgetsLocalizations.delegate,
      GlobalCupertinoLocalizations.delegate,
    ],
    home: Scaffold(body: child),
  );
}

void main() {
  group('PriceLabel', () {
    testWidgets('formats minor units to decimal + currency', (tester) async {
      await tester.pumpWidget(_wrap(
        const PriceLabel(
          money: CatalogMoney(amountMinor: 12050, currency: 'SAR'),
        ),
      ));
      expect(find.text('120.50 SAR'), findsOneWidget);
    });

    testWidgets('strikethrough decoration applies', (tester) async {
      await tester.pumpWidget(_wrap(
        const PriceLabel(
          money: CatalogMoney(amountMinor: 10000, currency: 'EGP'),
          strikethrough: true,
        ),
      ));
      final text = tester.widget<Text>(find.text('100.00 EGP'));
      expect(text.style?.decoration, TextDecoration.lineThrough);
    });
  });

  group('StockBadge', () {
    testWidgets('renders nothing when availability is null', (tester) async {
      await tester.pumpWidget(_wrap(const StockBadge(availability: null)));
      expect(find.byType(Text), findsNothing);
    });

    testWidgets('hides inStock by default', (tester) async {
      await tester.pumpWidget(_wrap(const StockBadge(
        availability: InventoryAvailability(
          productId: 'p-1',
          inStock: true,
          lowStock: false,
        ),
      )));
      expect(find.byType(Text), findsNothing);
    });

    testWidgets('shows out-of-stock', (tester) async {
      await tester.pumpWidget(_wrap(const StockBadge(
        availability: InventoryAvailability(
          productId: 'p-1',
          inStock: false,
          lowStock: false,
        ),
      )));
      expect(find.text('Out of stock'), findsOneWidget);
    });

    testWidgets('shows low-stock', (tester) async {
      await tester.pumpWidget(_wrap(const StockBadge(
        availability: InventoryAvailability(
          productId: 'p-1',
          inStock: true,
          lowStock: true,
        ),
      )));
      expect(find.text('Low stock'), findsOneWidget);
    });

    testWidgets('honors AR locale labels', (tester) async {
      await tester.pumpWidget(_wrap(
        const StockBadge(
          availability: InventoryAvailability(
            productId: 'p-1',
            inStock: false,
            lowStock: false,
          ),
          labels: StockBadgeLabels(outOfStock: 'غير متوفر'),
        ),
        locale: const Locale('ar'),
      ));
      expect(find.text('غير متوفر'), findsOneWidget);
    });
  });

  group('RatingBlock', () {
    testWidgets('renders nothing when ratingCount is 0', (tester) async {
      await tester.pumpWidget(_wrap(
        const RatingBlock(
          aggregate: ReviewsAggregate(
            productId: 'p-1',
            ratingAverage: 0,
            ratingCount: 0,
            starHistogram: [],
          ),
        ),
      ));
      expect(find.byIcon(Icons.star_rounded), findsNothing);
    });

    testWidgets('renders 4.7 (125) for non-zero aggregate', (tester) async {
      await tester.pumpWidget(_wrap(
        const RatingBlock(
          aggregate: ReviewsAggregate(
            productId: 'p-1',
            ratingAverage: 4.7,
            ratingCount: 125,
            starHistogram: [3, 7, 12, 28, 75],
          ),
        ),
      ));
      expect(find.text('4.7'), findsOneWidget);
      expect(find.text('(125)'), findsOneWidget);
    });

    testWidgets('shortens large counts to k-suffix', (tester) async {
      await tester.pumpWidget(_wrap(
        const RatingBlock(
          aggregate: ReviewsAggregate(
            productId: 'p-1',
            ratingAverage: 4.2,
            ratingCount: 1500,
            starHistogram: [],
          ),
        ),
      ));
      expect(find.text('(1.5k)'), findsOneWidget);
    });

    testWidgets('shows histogram when requested', (tester) async {
      await tester.pumpWidget(_wrap(
        const RatingBlock(
          aggregate: ReviewsAggregate(
            productId: 'p-1',
            ratingAverage: 4.0,
            ratingCount: 10,
            starHistogram: [1, 2, 3, 2, 2],
          ),
          showHistogram: true,
        ),
      ));
      expect(find.byType(LinearProgressIndicator), findsNWidgets(5));
    });
  });

  group('RestrictionGate', () {
    testWidgets('passes through when not restricted', (tester) async {
      await tester.pumpWidget(_wrap(
        RestrictionGate(
          isRestricted: false,
          onRequestVerification: () {},
          child: const Text('CTA'),
        ),
      ));
      expect(find.text('CTA'), findsOneWidget);
      expect(find.text('Verify to buy'), findsNothing);
    });

    testWidgets('compact: pill replaces CTA, taps fire callback',
        (tester) async {
      var taps = 0;
      await tester.pumpWidget(_wrap(
        RestrictionGate(
          isRestricted: true,
          compact: true,
          onRequestVerification: () => taps++,
          child: const Text('CTA'),
        ),
      ));
      expect(find.text('Verify to buy'), findsOneWidget);
      expect(find.text('CTA'), findsNothing);
      await tester.tap(find.text('Verify to buy'));
      expect(taps, 1);
    });

    testWidgets('full: disables underlying CTA + shows explainer',
        (tester) async {
      var taps = 0;
      var ctaTaps = 0;
      await tester.pumpWidget(_wrap(
        RestrictionGate(
          isRestricted: true,
          onRequestVerification: () => taps++,
          child: FilledButton(
            onPressed: () => ctaTaps++,
            child: const Text('Add to cart'),
          ),
        ),
      ));
      // CTA is wrapped in AbsorbPointer + Opacity but still rendered.
      expect(find.text('Add to cart'), findsOneWidget);
      // Tapping the CTA does not trigger its onPressed — AbsorbPointer
      // swallows the gesture, so the warning about missed hit-test is
      // the expected outcome here.
      await tester.tap(find.text('Add to cart'), warnIfMissed: false);
      expect(ctaTaps, 0);
      // The Verify link fires verification.
      await tester.tap(find.text('Verify'));
      expect(taps, 1);
    });
  });

  group('FilterBar', () {
    testWidgets('sort changes invoke callback with new value', (tester) async {
      CatalogSort? captured;
      await tester.pumpWidget(_wrap(
        FilterBar(
          sort: CatalogSort.relevance,
          locale: 'en',
          onSortChanged: (s) => captured = s,
        ),
      ));
      // Dropdown form field shows the current label.
      expect(find.text('Relevance'), findsOneWidget);
      await tester.tap(find.text('Relevance'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Price low to high').last);
      await tester.pumpAndSettle();
      expect(captured, CatalogSort.priceAsc);
    });

    testWidgets('renders brand dropdown only when brands non-empty',
        (tester) async {
      await tester.pumpWidget(_wrap(FilterBar(
        sort: CatalogSort.relevance,
        locale: 'en',
        onSortChanged: (_) {},
        brands: const [
          CatalogBrand(
            id: 'b-1',
            slug: 'brand-x',
            name: LocalizedText(en: 'Brand X'),
          ),
        ],
        onBrandChanged: (_) {},
      )));
      expect(find.text('Brand'), findsOneWidget);
    });
  });

  group('ProductCard', () {
    testWidgets('renders name + price; tap fires onTap; restricted → pill',
        (tester) async {
      var navTaps = 0;
      var cartTaps = 0;
      var verifyTaps = 0;
      // Cards live inside a grid; constrain width for a realistic layout
      // and prevent the 1:1 image AspectRatio from overflowing the test
      // viewport.
      await tester.pumpWidget(_wrap(
        Center(
          child: SizedBox(
            width: 200,
            child: ProductCard(
              product: const CatalogProduct(
                id: 'p-1',
                slug: 'tile-a',
                name: LocalizedText(en: 'Tile A', ar: 'بلاط أ'),
                thumbnailUrl: '',
                priceHint: CatalogMoney(amountMinor: 12000, currency: 'SAR'),
                isRestricted: true,
              ),
              locale: 'ar',
              onTap: () => navTaps++,
              onAddToCart: () => cartTaps++,
              onRequestVerification: () => verifyTaps++,
            ),
          ),
        ),
      ));
      expect(find.text('بلاط أ'), findsOneWidget);
      expect(find.text('120.00 SAR'), findsOneWidget);
      expect(find.text('Verify to buy'), findsOneWidget);
      // Tap on the verify pill (use the pill InkWell, not the outer card).
      await tester.tap(find.text('Verify to buy'));
      expect(verifyTaps, 1);
      // The card outer InkWell still fires onTap.
      await tester.tap(find.text('بلاط أ'));
      expect(navTaps, 1);
      // Add-to-cart was never reachable while restricted.
      expect(cartTaps, 0);
    });
  });
}
