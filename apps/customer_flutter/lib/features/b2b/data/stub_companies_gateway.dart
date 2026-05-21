import 'companies_gateway.dart';
import 'models/company_models.dart';

/// Deterministic in-memory [CompaniesGateway] for offline dev.
class StubCompaniesGateway implements CompaniesGateway {
  StubCompaniesGateway({DateTime? now})
      : _now = now ?? DateTime.utc(2026, 5, 20);

  final DateTime _now;
  final Map<String, Company> _companies = {};

  static String _short(String key) =>
      key.length >= 8 ? key.substring(0, 8) : key;

  Company _seed(String id) {
    return Company(
      id: id,
      name: 'Stub Dental Clinic',
      vatNumber: '310000000000003',
      address: 'King Fahd Road, Riyadh',
      commercialRegistration: '1010000000',
      marketCode: 'SA',
      myRole: 'admin',
      branches: const [
        Branch(id: 'br-1', name: 'Main', address: 'King Fahd Road'),
      ],
      memberships: const [
        Membership(id: 'm-1', userId: 'u-1', name: 'Owner', role: 'admin'),
        Membership(id: 'm-2', userId: 'u-2', name: 'Buyer User', role: 'buyer'),
      ],
    );
  }

  @override
  Future<CreateCompanyResult> create({
    required CreateCompanyRequest request,
    required String idempotencyKey,
  }) async {
    final id = 'co-${_short(idempotencyKey)}';
    _companies[id] = Company(
      id: id,
      name: request.name,
      vatNumber: request.vatNumber,
      address: request.address,
      commercialRegistration: request.commercialRegistration,
      marketCode: request.marketCode,
      myRole: 'admin',
      branches: const [],
      memberships: const [],
    );
    return CreateCompanyResult(id: id, name: request.name, createdAt: _now);
  }

  @override
  Future<Company> getById(String id) async {
    return _companies[id] ??= _seed(id);
  }

  @override
  Future<Company> update({
    required String id,
    required UpdateCompanyRequest request,
  }) async {
    final existing = await getById(id);
    final next = Company(
      id: existing.id,
      name: request.name ?? existing.name,
      vatNumber: request.vatNumber ?? existing.vatNumber,
      address: request.address ?? existing.address,
      commercialRegistration:
          request.commercialRegistration ?? existing.commercialRegistration,
      marketCode: existing.marketCode,
      myRole: existing.myRole,
      branches: existing.branches,
      memberships: existing.memberships,
    );
    _companies[id] = next;
    return next;
  }

  @override
  Future<Branch> addBranch({
    required String companyId,
    required CreateBranchRequest request,
  }) async {
    final existing = await getById(companyId);
    final branch = Branch(
      id: 'br-${existing.branches.length + 1}',
      name: request.name,
      address: request.address,
    );
    _companies[companyId] = Company(
      id: existing.id,
      name: existing.name,
      vatNumber: existing.vatNumber,
      address: existing.address,
      commercialRegistration: existing.commercialRegistration,
      marketCode: existing.marketCode,
      myRole: existing.myRole,
      branches: [...existing.branches, branch],
      memberships: existing.memberships,
    );
    return branch;
  }

  @override
  Future<void> deleteBranch({
    required String companyId,
    required String branchId,
  }) async {
    final existing = await getById(companyId);
    _companies[companyId] = Company(
      id: existing.id,
      name: existing.name,
      vatNumber: existing.vatNumber,
      address: existing.address,
      commercialRegistration: existing.commercialRegistration,
      marketCode: existing.marketCode,
      myRole: existing.myRole,
      branches: existing.branches
          .where((b) => b.id != branchId)
          .toList(growable: false),
      memberships: existing.memberships,
    );
  }

  @override
  Future<CreateInvitationResult> invite({
    required String companyId,
    required CreateInvitationRequest request,
  }) async {
    return CreateInvitationResult(
      invitationId: 'inv-${request.email.hashCode.abs() % 1000000}',
      email: request.email,
      role: request.role,
      sentAt: _now,
    );
  }

  @override
  Future<AcceptInvitationResult> acceptInvitation(String token) async {
    return const AcceptInvitationResult(companyId: 'co-stub', role: 'buyer');
  }

  @override
  Future<void> declineInvitation(String token) async {}

  @override
  Future<Membership> updateMembership({
    required String companyId,
    required String membershipId,
    required UpdateMembershipRequest request,
  }) async {
    final existing = await getById(companyId);
    final updated = existing.memberships.map((m) {
      if (m.id != membershipId) return m;
      return Membership(
          id: m.id, userId: m.userId, name: m.name, role: request.role);
    }).toList(growable: false);
    _companies[companyId] = Company(
      id: existing.id,
      name: existing.name,
      vatNumber: existing.vatNumber,
      address: existing.address,
      commercialRegistration: existing.commercialRegistration,
      marketCode: existing.marketCode,
      myRole: existing.myRole,
      branches: existing.branches,
      memberships: updated,
    );
    return updated.firstWhere(
      (m) => m.id == membershipId,
      orElse: () => Membership(
        id: membershipId,
        userId: '',
        name: '',
        role: request.role,
      ),
    );
  }

  @override
  Future<void> deleteMembership({
    required String companyId,
    required String membershipId,
  }) async {
    final existing = await getById(companyId);
    _companies[companyId] = Company(
      id: existing.id,
      name: existing.name,
      vatNumber: existing.vatNumber,
      address: existing.address,
      commercialRegistration: existing.commercialRegistration,
      marketCode: existing.marketCode,
      myRole: existing.myRole,
      branches: existing.branches,
      memberships: existing.memberships
          .where((m) => m.id != membershipId)
          .toList(growable: false),
    );
  }
}
