import 'dart:typed_data';

import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/returns/bloc/returns_list_bloc.dart';
import 'package:customer_flutter/features/returns/data/models/return_models.dart';
import 'package:customer_flutter/features/returns/data/returns_gateway.dart';
import 'package:flutter_test/flutter_test.dart';

class _FakeGateway implements ReturnsGateway {
  _FakeGateway(this._page);
  final ReturnListPage _page;
  final List<ReturnsListFilter> filters = [];

  @override
  Future<ReturnListPage> list(ReturnsListFilter filter) async {
    filters.add(filter);
    return _page;
  }

  @override
  Future<ReturnDetail> getById(String id) => throw UnimplementedError();
  @override
  Future<ReturnPhotoUploadResult> uploadPhoto({
    required Uint8List bytes,
    required String filename,
    required String clientPhotoKey,
  }) =>
      throw UnimplementedError();
  @override
  Future<CreateReturnResult> create({
    required String orderId,
    required CreateReturnRequest request,
    required String idempotencyKey,
  }) =>
      throw UnimplementedError();
}

class _ErrorGateway implements ReturnsGateway {
  @override
  Future<ReturnListPage> list(ReturnsListFilter filter) async =>
      throw Exception('boom');
  @override
  Future<ReturnDetail> getById(String id) => throw UnimplementedError();
  @override
  Future<ReturnPhotoUploadResult> uploadPhoto({
    required Uint8List bytes,
    required String filename,
    required String clientPhotoKey,
  }) =>
      throw UnimplementedError();
  @override
  Future<CreateReturnResult> create({
    required String orderId,
    required CreateReturnRequest request,
    required String idempotencyKey,
  }) =>
      throw UnimplementedError();
}

ReturnListItem _item(String id) => ReturnListItem(
      id: id,
      returnNumber: 'R-$id',
      orderId: 'order-$id',
      orderNumber: 'order-$id',
      createdAt: DateTime.utc(2026, 5, 1),
      state: 'pending',
    );

void main() {
  group('ReturnsListBloc', () {
    blocTest<ReturnsListBloc, ReturnsListState>(
      'loads + emits Empty when the page is empty',
      build: () => ReturnsListBloc(
        gateway: _FakeGateway(const ReturnListPage(
          items: [],
          page: 1,
          pageSize: 20,
          totalCount: 0,
        )),
      ),
      act: (bloc) => bloc.add(const ReturnsListStarted()),
      expect: () => [
        isA<ReturnsListLoading>(),
        isA<ReturnsListEmpty>(),
      ],
    );

    blocTest<ReturnsListBloc, ReturnsListState>(
      'loads + emits Loaded when items arrive',
      build: () => ReturnsListBloc(
        gateway: _FakeGateway(ReturnListPage(
          items: [_item('1')],
          page: 1,
          pageSize: 20,
          totalCount: 1,
        )),
      ),
      act: (bloc) => bloc.add(const ReturnsListStarted()),
      expect: () => [
        isA<ReturnsListLoading>(),
        isA<ReturnsListLoaded>().having((s) => s.items.length, 'items', 1),
      ],
    );

    blocTest<ReturnsListBloc, ReturnsListState>(
      'filter change resets page to 1 and re-queries with new status',
      build: () {
        final gw = _FakeGateway(ReturnListPage(
          items: [_item('1')],
          page: 1,
          pageSize: 20,
          totalCount: 1,
        ));
        return ReturnsListBloc(gateway: gw);
      },
      act: (bloc) => bloc.add(const ReturnsListFilterChanged('issued')),
      expect: () => [
        isA<ReturnsListLoading>()
            .having((s) => s.filter.status, 'filter.status', 'issued'),
        isA<ReturnsListLoaded>(),
      ],
    );

    blocTest<ReturnsListBloc, ReturnsListState>(
      'gateway error surfaces as Failure',
      build: () => ReturnsListBloc(gateway: _ErrorGateway()),
      act: (bloc) => bloc.add(const ReturnsListStarted()),
      expect: () => [
        isA<ReturnsListLoading>(),
        isA<ReturnsListFailure>(),
      ],
    );
  });
}
