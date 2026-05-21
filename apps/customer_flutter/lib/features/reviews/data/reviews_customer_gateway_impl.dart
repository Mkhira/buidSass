import 'package:dio/dio.dart';

import '../../../core/api/idempotency_interceptor.dart';
import '../../../core/error/error_mapper.dart';
import 'models/review_models.dart';
import 'reviews_customer_gateway.dart';

/// Dio-backed [ReviewsCustomerGateway]. Mirrors the
/// `ReturnsGatewayImpl` shape — idempotency via interceptor extra,
/// errors mapped via [ErrorMapper].
class ReviewsCustomerGatewayImpl implements ReviewsCustomerGateway {
  ReviewsCustomerGatewayImpl({required Dio dio, ErrorMapper? errorMapper})
      : _dio = dio,
        _errors = errorMapper ?? const ErrorMapper();

  static const _root = '/v1/customer/reviews';

  final Dio _dio;
  final ErrorMapper _errors;

  @override
  Future<CreateReviewResult> submit({
    required CreateReviewRequest request,
    required String idempotencyKey,
  }) async {
    try {
      final res = await _dio.post<Object?>(
        _root,
        data: request.toJson(),
        options: Options(
          extra: {IdempotencyInterceptor.extraKey: idempotencyKey},
        ),
      );
      return CreateReviewResult.fromJson(_asMap(res.data, 'reviews/submit'));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<MyReviewsPage> listMine(MyReviewsFilter filter) async {
    try {
      final res = await _dio.get<Object?>(
        '$_root/me',
        queryParameters: filter.toQuery(),
      );
      return MyReviewsPage.fromJson(_asMap(res.data, 'reviews/me'));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<MyReviewDetail> getMine(String reviewId) async {
    final encoded = Uri.encodeComponent(reviewId);
    try {
      final res = await _dio.get<Object?>('$_root/me/$encoded');
      return MyReviewDetail.fromJson(_asMap(res.data, 'reviews/me/$encoded'));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<MyReviewDetail> edit({
    required String reviewId,
    required EditReviewRequest request,
  }) async {
    final encoded = Uri.encodeComponent(reviewId);
    try {
      final res = await _dio.patch<Object?>(
        '$_root/$encoded',
        data: request.toJson(),
      );
      return MyReviewDetail.fromJson(_asMap(res.data, 'reviews/$encoded'));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<List<ReportReason>> getReportReasons() async {
    try {
      final res = await _dio.get<Object?>('$_root/report-reasons');
      final raw = res.data;
      if (raw is List) {
        return raw
            .whereType<Map>()
            .map((m) => ReportReason.fromJson(Map<String, Object?>.from(m)))
            .toList(growable: false);
      }
      return const [];
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<ReportReviewResult> report({
    required String reviewId,
    required ReportReviewRequest request,
  }) async {
    final encoded = Uri.encodeComponent(reviewId);
    try {
      final res = await _dio.post<Object?>(
        '$_root/$encoded/report',
        data: request.toJson(),
      );
      return ReportReviewResult.fromJson(
        _asMap(res.data, 'reviews/$encoded/report'),
      );
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  Map<String, Object?> _asMap(Object? raw, String label) {
    if (raw is! Map) {
      throw DioException(
        requestOptions: RequestOptions(path: label),
        type: DioExceptionType.badResponse,
        error: 'Malformed $label payload',
      );
    }
    return Map<String, Object?>.from(raw);
  }
}
