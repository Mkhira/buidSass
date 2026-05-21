import 'package:customer_flutter/core/api/idempotency_interceptor.dart';
import 'package:customer_flutter/features/b2b/data/companies_gateway_impl.dart';
import 'package:customer_flutter/features/b2b/data/models/company_models.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

typedef _Handler = Object? Function(RequestOptions opts);

class _Stub extends Interceptor {
  _Stub(this.handler);
  final _Handler handler;
  final List<RequestOptions> requests = [];

  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler h) {
    requests.add(options);
    final result = handler(options);
    if (result is DioException) {
      h.reject(result);
      return;
    }
    h.resolve(Response<Object?>(
      requestOptions: options,
      statusCode: result == null ? 204 : 200,
      data: result,
    ));
  }
}

({Dio dio, _Stub stub}) _build(_Handler handler) {
  final dio = Dio(BaseOptions(baseUrl: 'https://example.test'));
  dio.interceptors.add(const IdempotencyInterceptor());
  final stub = _Stub(handler);
  dio.interceptors.add(stub);
  return (dio: dio, stub: stub);
}

Map<String, Object?> _companyPayload() => {
      'id': 'co-1',
      'name': 'Stub Clinic',
      'vatNumber': '310000000000003',
      'address': 'Riyadh',
      'commercialRegistration': '1010',
      'marketCode': 'SA',
      'myRole': 'admin',
      'branches': [
        {'id': 'br-1', 'name': 'Main', 'address': 'King Fahd Rd'},
      ],
      'memberships': [
        {
          'id': 'm-1',
          'userId': 'u-1',
          'name': 'Owner',
          'role': 'admin',
        }
      ],
    };

void main() {
  test('create sends Idempotency-Key', () async {
    final pair = _build((_) => {
          'id': 'co-1',
          'name': 'Stub Clinic',
          'createdAt': '2026-05-01T00:00:00Z',
        });
    final gw = CompaniesGatewayImpl(dio: pair.dio);
    final r = await gw.create(
      request: const CreateCompanyRequest(
        name: 'Stub Clinic',
        vatNumber: '310000000000003',
        address: 'Riyadh',
        marketCode: 'SA',
      ),
      idempotencyKey: 'co-key-1',
    );
    expect(
      pair.stub.requests.single.headers[IdempotencyInterceptor.headerName],
      'co-key-1',
    );
    expect(r.id, 'co-1');
  });

  test('getById decodes branches + memberships + myRole', () async {
    final pair = _build((_) => _companyPayload());
    final gw = CompaniesGatewayImpl(dio: pair.dio);
    final c = await gw.getById('co-1');
    expect(c.isAdmin, isTrue);
    expect(c.branches.single.name, 'Main');
    expect(c.memberships.single.role, 'admin');
  });

  test('update PATCHes diff', () async {
    final pair = _build((opts) {
      expect(opts.method, 'PATCH');
      return _companyPayload();
    });
    final gw = CompaniesGatewayImpl(dio: pair.dio);
    await gw.update(
      id: 'co-1',
      request: const UpdateCompanyRequest(name: 'Updated'),
    );
  });

  test('addBranch posts to /branches', () async {
    final pair = _build((_) => {
          'id': 'br-2',
          'name': 'North',
          'address': 'Riyadh',
        });
    final gw = CompaniesGatewayImpl(dio: pair.dio);
    final b = await gw.addBranch(
      companyId: 'co-1',
      request: const CreateBranchRequest(name: 'North', address: 'Riyadh'),
    );
    expect(b.name, 'North');
    expect(
      pair.stub.requests.single.path,
      '/api/customer/companies/co-1/branches',
    );
  });

  test('deleteBranch issues DELETE', () async {
    final pair = _build((opts) {
      expect(opts.method, 'DELETE');
      return null;
    });
    final gw = CompaniesGatewayImpl(dio: pair.dio);
    await gw.deleteBranch(companyId: 'co-1', branchId: 'br-1');
    expect(
      pair.stub.requests.single.path,
      '/api/customer/companies/co-1/branches/br-1',
    );
  });

  test('invite posts email + role', () async {
    final pair = _build((_) => {
          'invitationId': 'inv-1',
          'email': 'a@b.com',
          'role': 'buyer',
          'sentAt': '2026-05-01T00:00:00Z',
        });
    final gw = CompaniesGatewayImpl(dio: pair.dio);
    final r = await gw.invite(
      companyId: 'co-1',
      request: const CreateInvitationRequest(
        email: 'a@b.com',
        role: 'buyer',
      ),
    );
    expect(r.role, 'buyer');
  });

  test('acceptInvitation returns companyId + role', () async {
    final pair = _build((_) => {'companyId': 'co-1', 'role': 'buyer'});
    final gw = CompaniesGatewayImpl(dio: pair.dio);
    final r = await gw.acceptInvitation('tok-123');
    expect(r.companyId, 'co-1');
    expect(
      pair.stub.requests.single.path,
      '/api/customer/companies/invitations/tok-123/accept',
    );
  });

  test('declineInvitation posts to /decline', () async {
    final pair = _build((_) => null);
    final gw = CompaniesGatewayImpl(dio: pair.dio);
    await gw.declineInvitation('tok-123');
    expect(
      pair.stub.requests.single.path,
      '/api/customer/companies/invitations/tok-123/decline',
    );
  });

  test('updateMembership PATCHes role', () async {
    final pair = _build((opts) {
      expect(opts.method, 'PATCH');
      return {
        'id': 'm-1',
        'userId': 'u-1',
        'name': 'Owner',
        'role': 'approver',
      };
    });
    final gw = CompaniesGatewayImpl(dio: pair.dio);
    final m = await gw.updateMembership(
      companyId: 'co-1',
      membershipId: 'm-1',
      request: const UpdateMembershipRequest(role: 'approver'),
    );
    expect(m.role, 'approver');
  });

  test('deleteMembership issues DELETE', () async {
    final pair = _build((opts) {
      expect(opts.method, 'DELETE');
      return null;
    });
    final gw = CompaniesGatewayImpl(dio: pair.dio);
    await gw.deleteMembership(companyId: 'co-1', membershipId: 'm-1');
  });
}
