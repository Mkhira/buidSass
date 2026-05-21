import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/reviews/bloc/review_submit_bloc.dart';
import 'package:customer_flutter/features/reviews/data/models/review_models.dart';
import 'package:customer_flutter/features/reviews/data/reviews_customer_gateway.dart';
import 'package:flutter_test/flutter_test.dart';

class _FakeGateway implements ReviewsCustomerGateway {
  _FakeGateway({this.throwWith});
  final Object? throwWith;
  CreateReviewRequest? lastRequest;
  String? lastIdempotencyKey;

  @override
  Future<CreateReviewResult> submit({
    required CreateReviewRequest request,
    required String idempotencyKey,
  }) async {
    lastRequest = request;
    lastIdempotencyKey = idempotencyKey;
    if (throwWith != null) throw throwWith!;
    return CreateReviewResult(
      id: 'rv-1',
      state: 'pending_moderation',
      createdAt: DateTime.utc(2026, 5, 20),
    );
  }

  // unused
  @override
  Future<MyReviewsPage> listMine(MyReviewsFilter filter) =>
      throw UnimplementedError();
  @override
  Future<MyReviewDetail> getMine(String reviewId) => throw UnimplementedError();
  @override
  Future<MyReviewDetail> edit({
    required String reviewId,
    required EditReviewRequest request,
  }) =>
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

void main() {
  blocTest<ReviewSubmitBloc, ReviewSubmitState>(
    'started sets product/order/locale on the form',
    build: () => ReviewSubmitBloc(
      gateway: _FakeGateway(),
      idempotencyKeyFactory: () => 'k1',
    ),
    act: (b) => b.add(const ReviewSubmitStarted(
      productId: 'p-1',
      orderId: 'o-1',
      locale: 'ar',
    )),
    expect: () => [
      isA<ReviewSubmitForm>()
          .having((s) => s.productId, 'productId', 'p-1')
          .having((s) => s.locale, 'locale', 'ar'),
    ],
  );

  blocTest<ReviewSubmitBloc, ReviewSubmitState>(
    'comment is capped at 2000 characters',
    build: () => ReviewSubmitBloc(
      gateway: _FakeGateway(),
      idempotencyKeyFactory: () => 'k1',
    ),
    act: (b) async {
      b.add(const ReviewSubmitStarted(
        productId: 'p-1',
        orderId: 'o-1',
        locale: 'en',
      ));
      await Future<void>.delayed(Duration.zero);
      b.add(ReviewSubmitCommentChanged('x' * 5000));
    },
    skip: 1,
    expect: () => [
      isA<ReviewSubmitForm>().having((s) => s.comment.length, 'len', 2000),
    ],
  );

  blocTest<ReviewSubmitBloc, ReviewSubmitState>(
    'submit blocked when canSubmit=false',
    build: () => ReviewSubmitBloc(
      gateway: _FakeGateway(),
      idempotencyKeyFactory: () => 'k1',
    ),
    act: (b) async {
      b.add(const ReviewSubmitStarted(
        productId: 'p-1',
        orderId: 'o-1',
        locale: 'en',
      ));
      await Future<void>.delayed(Duration.zero);
      b.add(const ReviewSubmitSubmitted());
    },
    skip: 1,
    expect: () => [
      isA<ReviewSubmitForm>()
          .having((s) => s.formError, 'formError', isNotNull),
    ],
  );

  blocTest<ReviewSubmitBloc, ReviewSubmitState>(
    'happy path → submitting → done with Idempotency-Key',
    build: () => ReviewSubmitBloc(
      gateway: _FakeGateway(),
      idempotencyKeyFactory: () => 'rev-key-1',
    ),
    act: (b) async {
      b.add(const ReviewSubmitStarted(
        productId: 'p-1',
        orderId: 'o-1',
        locale: 'en',
      ));
      await Future<void>.delayed(Duration.zero);
      b.add(const ReviewSubmitRatingChanged(5));
      b.add(const ReviewSubmitCommentChanged('Solid'));
      await Future<void>.delayed(Duration.zero);
      b.add(const ReviewSubmitSubmitted());
    },
    skip: 3,
    expect: () => [
      isA<ReviewSubmitSubmitting>(),
      isA<ReviewSubmitDone>(),
    ],
    verify: (b) => expect(b.idempotencyKey, 'rev-key-1'),
  );

  blocTest<ReviewSubmitBloc, ReviewSubmitState>(
    '403 from server → NotEligible',
    build: () => ReviewSubmitBloc(
      gateway: _FakeGateway(throwWith: Exception('forbidden: http.403')),
      idempotencyKeyFactory: () => 'k1',
    ),
    act: (b) async {
      b.add(const ReviewSubmitStarted(
        productId: 'p-1',
        orderId: 'o-1',
        locale: 'en',
      ));
      await Future<void>.delayed(Duration.zero);
      b.add(const ReviewSubmitRatingChanged(5));
      b.add(const ReviewSubmitCommentChanged('Solid'));
      await Future<void>.delayed(Duration.zero);
      b.add(const ReviewSubmitSubmitted());
    },
    skip: 3,
    expect: () => [
      isA<ReviewSubmitSubmitting>(),
      isA<ReviewSubmitNotEligible>(),
    ],
  );
}
