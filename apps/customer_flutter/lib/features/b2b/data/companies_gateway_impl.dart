import 'package:dio/dio.dart';

import '../../../core/api/idempotency_interceptor.dart';
import '../../../core/error/error_mapper.dart';
import 'companies_gateway.dart';
import 'models/company_models.dart';

class CompaniesGatewayImpl implements CompaniesGateway {
  CompaniesGatewayImpl({required Dio dio, ErrorMapper? errorMapper})
      : _dio = dio,
        _errors = errorMapper ?? const ErrorMapper();

  static const _root = '/api/customer/companies';

  final Dio _dio;
  final ErrorMapper _errors;

  @override
  Future<CreateCompanyResult> create({
    required CreateCompanyRequest request,
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
      return CreateCompanyResult.fromJson(_asMap(res.data, 'companies/create'));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<Company> getById(String id) async {
    final encoded = Uri.encodeComponent(id);
    try {
      final res = await _dio.get<Object?>('$_root/$encoded');
      return Company.fromJson(_asMap(res.data, 'companies/$encoded'));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<Company> update({
    required String id,
    required UpdateCompanyRequest request,
  }) async {
    final encoded = Uri.encodeComponent(id);
    try {
      final res = await _dio.patch<Object?>(
        '$_root/$encoded',
        data: request.toJson(),
      );
      return Company.fromJson(_asMap(res.data, 'companies/$encoded'));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<Branch> addBranch({
    required String companyId,
    required CreateBranchRequest request,
  }) async {
    final encoded = Uri.encodeComponent(companyId);
    try {
      final res = await _dio.post<Object?>(
        '$_root/$encoded/branches',
        data: request.toJson(),
      );
      return Branch.fromJson(_asMap(res.data, 'companies/$encoded/branches'));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<void> deleteBranch({
    required String companyId,
    required String branchId,
  }) async {
    final cid = Uri.encodeComponent(companyId);
    final bid = Uri.encodeComponent(branchId);
    try {
      await _dio.delete<void>('$_root/$cid/branches/$bid');
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<CreateInvitationResult> invite({
    required String companyId,
    required CreateInvitationRequest request,
  }) async {
    final encoded = Uri.encodeComponent(companyId);
    try {
      final res = await _dio.post<Object?>(
        '$_root/$encoded/invitations',
        data: request.toJson(),
      );
      return CreateInvitationResult.fromJson(
        _asMap(res.data, 'companies/$encoded/invitations'),
      );
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<AcceptInvitationResult> acceptInvitation(String token) async {
    final encoded = Uri.encodeComponent(token);
    try {
      final res = await _dio.post<Object?>(
        '$_root/invitations/$encoded/accept',
      );
      return AcceptInvitationResult.fromJson(
        _asMap(res.data, 'invitations/$encoded/accept'),
      );
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<void> declineInvitation(String token) async {
    final encoded = Uri.encodeComponent(token);
    try {
      await _dio.post<void>('$_root/invitations/$encoded/decline');
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<Membership> updateMembership({
    required String companyId,
    required String membershipId,
    required UpdateMembershipRequest request,
  }) async {
    final cid = Uri.encodeComponent(companyId);
    final mid = Uri.encodeComponent(membershipId);
    try {
      final res = await _dio.patch<Object?>(
        '$_root/$cid/memberships/$mid',
        data: request.toJson(),
      );
      return Membership.fromJson(
        _asMap(res.data, 'companies/$cid/memberships/$mid'),
      );
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<void> deleteMembership({
    required String companyId,
    required String membershipId,
  }) async {
    final cid = Uri.encodeComponent(companyId);
    final mid = Uri.encodeComponent(membershipId);
    try {
      await _dio.delete<void>('$_root/$cid/memberships/$mid');
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
