import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/search/bloc/lookup_bloc.dart';
import 'package:customer_flutter/features/search/data/models/search_models.dart';
import 'package:customer_flutter/features/search/data/search_gateway.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockGateway extends Mock implements SearchGateway {}

void main() {
  setUpAll(() {
    registerFallbackValue(
      const LookupRequest(sku: 'x', marketCode: ''),
    );
  });

  late _MockGateway gateway;

  LookupBloc build() =>
      LookupBloc(gateway: gateway, marketProvider: () => 'ksa');

  setUp(() => gateway = _MockGateway());

  blocTest<LookupBloc, LookupState>(
    'LookupSubmitted with hit emits Looking then Matched',
    build: () {
      when(() => gateway.lookup(any())).thenAnswer((_) async {
        return const LookupResult(
          matched: true,
          match: LookupMatch(
            productId: 'p-1',
            slug: 'tile-a',
            name: 'Tile A',
            kind: 'sku',
          ),
        );
      });
      return build();
    },
    act: (b) => b.add(const LookupSubmitted(value: 'SKU-1', kind: 'sku')),
    expect: () => [
      isA<LookupLooking>(),
      isA<LookupMatched>().having((s) => s.slug, 'slug', 'tile-a'),
    ],
  );

  blocTest<LookupBloc, LookupState>(
    'No-match payload emits LookupNoMatch',
    build: () {
      when(() => gateway.lookup(any())).thenAnswer((_) async {
        return const LookupResult(matched: false);
      });
      return build();
    },
    act: (b) => b.add(const LookupSubmitted(value: 'NOTFOUND', kind: 'sku')),
    expect: () => [isA<LookupLooking>(), isA<LookupNoMatch>()],
  );

  blocTest<LookupBloc, LookupState>(
    'LookupScanRequested without permission emits PermissionDenied',
    build: build,
    act: (b) => b.add(const LookupScanRequested(permissionGranted: false)),
    expect: () => [isA<LookupPermissionDenied>()],
  );

  blocTest<LookupBloc, LookupState>(
    'LookupScanRequested with permission emits Scanning',
    build: build,
    act: (b) => b.add(const LookupScanRequested(permissionGranted: true)),
    expect: () => [isA<LookupScanning>()],
  );

  blocTest<LookupBloc, LookupState>(
    'Duplicate scan results inside 1s are debounced',
    build: () {
      when(() => gateway.lookup(any())).thenAnswer((_) async {
        return const LookupResult(
          matched: true,
          match: LookupMatch(
              productId: 'p-1', slug: 's', name: 'n', kind: 'barcode'),
        );
      });
      return build();
    },
    act: (b) async {
      b.add(const LookupScanResult('CODE-1'));
      b.add(const LookupScanResult('CODE-1'));
    },
    wait: const Duration(milliseconds: 50),
    expect: () => [isA<LookupLooking>(), isA<LookupMatched>()],
    verify: (_) {
      // Only the first scan should reach the gateway; the second is the
      // 1-second debounce drop documented in S-3.4 edge cases.
      verify(() => gateway.lookup(any())).called(1);
    },
  );
}
