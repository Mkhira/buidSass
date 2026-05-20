import 'package:dio/dio.dart';

import '../../../core/error/error_mapper.dart';
import 'checkout_gateway.dart';
import 'models/checkout_models.dart';

/// Dio-backed [CheckoutGateway]. The hook that distinguishes this gateway
/// from the rest of the app's data layer is **409 drift handling**: every
/// PATCH/POST against an active session can return a checkout-drift
/// envelope, and we rethrow that as a typed [CheckoutDriftException] so
/// the bloc layer can branch on `try/on CheckoutDriftException` instead
/// of inspecting status codes.
class CheckoutGatewayImpl implements CheckoutGateway {
  CheckoutGatewayImpl({required Dio dio, ErrorMapper? errorMapper})
      : _dio = dio,
        _errors = errorMapper ?? const ErrorMapper();

  static const _sessions = '/v1/customer/checkout/sessions';
  static const _priceCart = '/v1/customer/pricing/price-cart';

  final Dio _dio;
  final ErrorMapper _errors;

  @override
  Future<CreateSessionResult> createSession(CreateSessionRequest request) {
    return _wrap(() async {
      final res = await _dio.post<Object?>(_sessions, data: request.toJson());
      return CreateSessionResult.fromJson(_asMap(res.data, _sessions));
    });
  }

  @override
  Future<CheckoutSummary> getSummary(String sessionId) {
    return _wrap(() async {
      final res = await _dio.get<Object?>('$_sessions/$sessionId/summary');
      return CheckoutSummary.fromJson(_asMap(res.data, 'summary'));
    });
  }

  @override
  Future<List<ShippingQuoteOption>> getShippingQuotes(String sessionId) {
    return _wrap(() async {
      final res =
          await _dio.get<Object?>('$_sessions/$sessionId/shipping-quotes');
      final raw = res.data;
      if (raw is! List) {
        throw DioException(
          requestOptions: RequestOptions(path: 'shipping-quotes'),
          type: DioExceptionType.badResponse,
          error: 'Malformed shipping-quotes payload',
        );
      }
      return raw
          .whereType<Map>()
          .map(
              (m) => ShippingQuoteOption.fromJson(Map<String, Object?>.from(m)))
          .toList(growable: false);
    });
  }

  @override
  Future<CheckoutSummary> patchAddress({
    required String sessionId,
    required CheckoutAddressDto address,
  }) {
    return _wrap(() async {
      final res = await _dio.patch<Object?>(
        '$_sessions/$sessionId/address',
        data: address.toJson(),
      );
      return CheckoutSummary.fromJson(_asMap(res.data, 'address'));
    });
  }

  @override
  Future<CheckoutSummary> patchShipping({
    required String sessionId,
    required String method,
  }) {
    return _wrap(() async {
      final res = await _dio.patch<Object?>(
        '$_sessions/$sessionId/shipping',
        data: {'method': method},
      );
      return CheckoutSummary.fromJson(_asMap(res.data, 'shipping'));
    });
  }

  @override
  Future<CheckoutSummary> patchPaymentMethod({
    required String sessionId,
    required String method,
    String? providerToken,
    String? bankTransferReference,
  }) {
    return _wrap(() async {
      final res = await _dio.patch<Object?>(
        '$_sessions/$sessionId/payment-method',
        data: {
          'method': method,
          if (providerToken != null) 'providerToken': providerToken,
          if (bankTransferReference != null)
            'bankTransferReference': bankTransferReference,
        },
      );
      return CheckoutSummary.fromJson(_asMap(res.data, 'payment-method'));
    });
  }

  @override
  Future<SubmitResult> submit({
    required String sessionId,
    required String idempotencyKey,
  }) {
    return _wrap(() async {
      final res = await _dio.post<Object?>(
        '$_sessions/$sessionId/submit',
        options: Options(
          // Hand the key to IdempotencyInterceptor (core/api). The
          // interceptor sets the actual `Idempotency-Key` header so
          // retries from the dio layer use the same value end-to-end.
          extra: {'idempotencyKey': idempotencyKey},
        ),
      );
      return SubmitResult.fromJson(_asMap(res.data, 'submit'));
    });
  }

  @override
  Future<CheckoutSummary> acceptDrift(String sessionId) {
    return _wrap(() async {
      final res =
          await _dio.post<Object?>('$_sessions/$sessionId/accept-drift');
      return CheckoutSummary.fromJson(_asMap(res.data, 'accept-drift'));
    });
  }

  @override
  Future<PriceCartResult> priceCart(PriceCartRequest request) {
    return _wrap(() async {
      final res = await _dio.post<Object?>(_priceCart, data: request.toJson());
      return PriceCartResult.fromJson(_asMap(res.data, _priceCart));
    });
  }

  // ---- helpers ----

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

  Future<T> _wrap<T>(Future<T> Function() body) async {
    try {
      return await body();
    } on DioException catch (e) {
      // 409 → CheckoutDriftException (BR-4). Anything else flows through
      // the standard ErrorMapper.
      final res = e.response;
      if (res?.statusCode == 409) {
        final body = res?.data;
        if (body is Map) {
          final error = body['error'];
          if (error is Map) {
            final details = error['details'];
            if (details is Map) {
              throw CheckoutDriftException(
                details:
                    DriftDetails.fromJson(Map<String, Object?>.from(details)),
                correlationId: error['correlationId'] as String?,
              );
            }
          }
        }
        // 409 without a parseable details payload — still a drift, but
        // empty deltas. Bloc shows a generic "Prices changed" body.
        throw CheckoutDriftException(
          details: const DriftDetails(deltas: []),
          correlationId: null,
        );
      }
      throw _errors.fromDio(e);
    }
  }
}
