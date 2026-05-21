import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/reviews/bloc/my_review_detail_bloc.dart';
import 'package:customer_flutter/features/reviews/data/models/review_models.dart';
import 'package:customer_flutter/features/reviews/data/reviews_customer_gateway.dart';
import 'package:flutter_test/flutter_test.dart';

class _FakeGateway implements ReviewsCustomerGateway {
  _FakeGateway({required this.detail, this.throwOnEdit = false});

  MyReviewDetail detail;
  bool throwOnEdit;

  @override
  Future<MyReviewDetail> getMine(String reviewId) async => detail;

  @override
  Future<MyReviewDetail> edit({
    required String reviewId,
    required EditReviewRequest request,
  }) async {
    if (throwOnEdit) throw Exception('boom');
    final next = MyReviewDetail(
      id: detail.id,
      productId: detail.productId,
      productName: detail.productName,
      rating: request.rating,
      comment: request.comment,
      state: detail.state,
      createdAt: detail.createdAt,
      media: detail.media,
      locale: detail.locale,
      editableUntil: detail.editableUntil,
      moderationNote: detail.moderationNote,
    );
    detail = next;
    return next;
  }

  // unused
  @override
  Future<CreateReviewResult> submit({
    required CreateReviewRequest request,
    required String idempotencyKey,
  }) =>
      throw UnimplementedError();
  @override
  Future<MyReviewsPage> listMine(MyReviewsFilter filter) =>
      throw UnimplementedError();
  @override
  Future<List<ReportReason>> getReportReasons() => throw UnimplementedError();
  @override
  Future<ReportReviewResult> report({
    required String reviewId,
    required ReportReviewRequest request,
  }) =>
      throw UnimplementedError();
}

MyReviewDetail _detail({
  int rating = 4,
  String comment = 'ok',
  DateTime? editableUntil,
}) {
  return MyReviewDetail(
    id: 'rv-1',
    productId: 'p-1',
    productName: 'Dental gel',
    rating: rating,
    comment: comment,
    state: 'visible',
    createdAt: DateTime.utc(2026, 5, 1),
    media: const [],
    locale: 'en',
    editableUntil: editableUntil,
  );
}

void main() {
  blocTest<MyReviewDetailBloc, MyReviewDetailState>(
    'started → loaded with rating/comment seeded from detail',
    build: () => MyReviewDetailBloc(
      gateway: _FakeGateway(detail: _detail(rating: 5, comment: 'Great')),
      reviewId: 'rv-1',
    ),
    act: (b) => b.add(const MyReviewDetailStarted()),
    expect: () => [
      isA<MyReviewDetailLoading>(),
      isA<MyReviewDetailLoaded>()
          .having((s) => s.rating, 'rating', 5)
          .having((s) => s.comment, 'comment', 'Great'),
    ],
  );

  blocTest<MyReviewDetailBloc, MyReviewDetailState>(
    'editToggled disallowed when editable window closed',
    build: () {
      return MyReviewDetailBloc(
        gateway: _FakeGateway(
          detail: _detail(
            editableUntil: DateTime.now().subtract(const Duration(days: 1)),
          ),
        ),
        reviewId: 'rv-1',
      );
    },
    act: (b) async {
      b.add(const MyReviewDetailStarted());
      await Future<void>.delayed(Duration.zero);
      b.add(const MyReviewDetailEditToggled());
    },
    skip: 2,
    expect: () => [],
  );

  blocTest<MyReviewDetailBloc, MyReviewDetailState>(
    'edit → save happy path → saved updates detail and exits edit mode',
    build: () => MyReviewDetailBloc(
      gateway: _FakeGateway(
        detail: _detail(
          editableUntil: DateTime.now().add(const Duration(days: 1)),
        ),
      ),
      reviewId: 'rv-1',
    ),
    act: (b) async {
      b.add(const MyReviewDetailStarted());
      await Future<void>.delayed(Duration.zero);
      b.add(const MyReviewDetailEditToggled());
      b.add(const MyReviewDetailRatingChanged(3));
      b.add(const MyReviewDetailCommentChanged('Updated'));
      await Future<void>.delayed(Duration.zero);
      b.add(const MyReviewDetailSaved());
    },
    skip: 5,
    expect: () => [
      isA<MyReviewDetailSaving>(),
      isA<MyReviewDetailLoaded>()
          .having((s) => s.detail.rating, 'detail.rating', 3)
          .having((s) => s.editing, 'editing', isFalse),
    ],
  );

  blocTest<MyReviewDetailBloc, MyReviewDetailState>(
    'save error preserves edits + sets saveError',
    build: () => MyReviewDetailBloc(
      gateway: _FakeGateway(
        detail: _detail(
          editableUntil: DateTime.now().add(const Duration(days: 1)),
        ),
        throwOnEdit: true,
      ),
      reviewId: 'rv-1',
    ),
    act: (b) async {
      b.add(const MyReviewDetailStarted());
      await Future<void>.delayed(Duration.zero);
      b.add(const MyReviewDetailEditToggled());
      b.add(const MyReviewDetailRatingChanged(3));
      b.add(const MyReviewDetailCommentChanged('Updated'));
      await Future<void>.delayed(Duration.zero);
      b.add(const MyReviewDetailSaved());
    },
    skip: 5,
    expect: () => [
      isA<MyReviewDetailSaving>(),
      isA<MyReviewDetailLoaded>()
          .having((s) => s.editing, 'editing', isTrue)
          .having((s) => s.rating, 'rating preserved', 3)
          .having((s) => s.saveError, 'saveError', isNotNull),
    ],
  );
}
