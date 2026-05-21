import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/reviews/bloc/report_review_bloc.dart';
import 'package:customer_flutter/features/reviews/data/models/review_models.dart';
import 'package:customer_flutter/features/reviews/data/reviews_customer_gateway.dart';
import 'package:flutter_test/flutter_test.dart';

class _FakeGateway implements ReviewsCustomerGateway {
  _FakeGateway({this.reasons = const [], this.throwOnReport = false});

  final List<ReportReason> reasons;
  final bool throwOnReport;

  ReportReviewRequest? lastRequest;
  String? lastReviewId;

  @override
  Future<List<ReportReason>> getReportReasons() async => reasons;

  @override
  Future<ReportReviewResult> report({
    required String reviewId,
    required ReportReviewRequest request,
  }) async {
    lastReviewId = reviewId;
    lastRequest = request;
    if (throwOnReport) throw Exception('boom');
    return const ReportReviewResult(id: 'rep-1', state: 'submitted');
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
  Future<MyReviewDetail> getMine(String reviewId) => throw UnimplementedError();
  @override
  Future<MyReviewDetail> edit({
    required String reviewId,
    required EditReviewRequest request,
  }) =>
      throw UnimplementedError();
}

void main() {
  blocTest<ReportReviewBloc, ReportReviewState>(
    'started → ready with server reasons',
    build: () => ReportReviewBloc(
      gateway: _FakeGateway(reasons: const [
        ReportReason(key: 'spam', label: 'Spam'),
        ReportReason(key: 'abuse', label: 'Abuse'),
      ]),
      reviewId: 'rv-1',
    ),
    act: (b) => b.add(const ReportReviewStarted()),
    expect: () => [
      isA<ReportReviewLoading>(),
      isA<ReportReviewReady>().having(
        (s) => s.reasons.length,
        'reasons',
        2,
      ),
    ],
  );

  blocTest<ReportReviewBloc, ReportReviewState>(
    'submit blocked without a reason',
    build: () => ReportReviewBloc(
      gateway: _FakeGateway(reasons: const [
        ReportReason(key: 'spam', label: 'Spam'),
      ]),
      reviewId: 'rv-1',
    ),
    act: (b) async {
      b.add(const ReportReviewStarted());
      await Future<void>.delayed(Duration.zero);
      b.add(const ReportReviewSubmitted());
    },
    skip: 2,
    expect: () => [],
  );

  blocTest<ReportReviewBloc, ReportReviewState>(
    'happy path → submitting → done',
    build: () => ReportReviewBloc(
      gateway: _FakeGateway(reasons: const [
        ReportReason(key: 'spam', label: 'Spam'),
      ]),
      reviewId: 'rv-1',
    ),
    act: (b) async {
      b.add(const ReportReviewStarted());
      await Future<void>.delayed(Duration.zero);
      b.add(const ReportReviewReasonSelected('spam'));
      b.add(const ReportReviewNoteChanged('looks fake'));
      await Future<void>.delayed(Duration.zero);
      b.add(const ReportReviewSubmitted());
    },
    skip: 4,
    expect: () => [
      isA<ReportReviewSubmitting>(),
      isA<ReportReviewDone>(),
    ],
  );

  blocTest<ReportReviewBloc, ReportReviewState>(
    'gateway throws → ready + formError',
    build: () => ReportReviewBloc(
      gateway: _FakeGateway(
        reasons: const [ReportReason(key: 'spam', label: 'Spam')],
        throwOnReport: true,
      ),
      reviewId: 'rv-1',
    ),
    act: (b) async {
      b.add(const ReportReviewStarted());
      await Future<void>.delayed(Duration.zero);
      b.add(const ReportReviewReasonSelected('spam'));
      await Future<void>.delayed(Duration.zero);
      b.add(const ReportReviewSubmitted());
    },
    skip: 3,
    expect: () => [
      isA<ReportReviewSubmitting>(),
      isA<ReportReviewReady>()
          .having((s) => s.formError, 'formError', isNotNull),
    ],
  );
}
