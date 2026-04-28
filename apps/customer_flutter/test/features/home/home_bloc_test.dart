import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/home/bloc/home_bloc.dart';
import 'package:customer_flutter/features/home/data/home_repository.dart';
import 'package:customer_flutter/features/home/data/home_view_models.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockRepo extends Mock implements HomeRepository {}

void main() {
  late _MockRepo repo;

  setUp(() => repo = _MockRepo());

  blocTest<HomeBloc, HomeState>(
    'Loading -> Loaded on success',
    build: () {
      when(repo.fetchHome).thenAnswer((_) async => const HomePayloadViewModel(
            banners: [
              HomeBannerViewModel(
                  id: 'b', titleKey: 't', imageUrl: '', deeplink: '/')
            ],
            featured: [],
            categories: [],
          ));
      return HomeBloc(repository: repo);
    },
    act: (b) => b.add(const HomeRequested()),
    expect: () => [
      isA<HomeLoading>(),
      isA<HomeLoaded>(),
    ],
  );

  blocTest<HomeBloc, HomeState>(
    'Loading -> Empty when payload empty',
    build: () {
      when(repo.fetchHome).thenAnswer((_) async => const HomePayloadViewModel(
            banners: [],
            featured: [],
            categories: [],
          ));
      return HomeBloc(repository: repo);
    },
    act: (b) => b.add(const HomeRequested()),
    expect: () => [isA<HomeLoading>(), isA<HomeEmpty>()],
  );

  blocTest<HomeBloc, HomeState>(
    'Loading -> Error on throw',
    build: () {
      when(repo.fetchHome).thenThrow(Exception('boom'));
      return HomeBloc(repository: repo);
    },
    act: (b) => b.add(const HomeRequested()),
    expect: () => [isA<HomeLoading>(), isA<HomeError>()],
  );
}
