import 'models/company_models.dart';

/// CompaniesGateway — the 7 company-side ops in `openapi.b2b.json`
/// (create / get / patch + branches / invitations / memberships) plus
/// the 2 token-bound invitation ops (`/invitations/{token}/accept` and
/// `/decline`).
abstract class CompaniesGateway {
  Future<CreateCompanyResult> create({
    required CreateCompanyRequest request,
    required String idempotencyKey,
  });

  Future<Company> getById(String id);

  Future<Company> update({
    required String id,
    required UpdateCompanyRequest request,
  });

  Future<Branch> addBranch({
    required String companyId,
    required CreateBranchRequest request,
  });

  Future<void> deleteBranch({
    required String companyId,
    required String branchId,
  });

  Future<CreateInvitationResult> invite({
    required String companyId,
    required CreateInvitationRequest request,
  });

  Future<AcceptInvitationResult> acceptInvitation(String token);

  Future<void> declineInvitation(String token);

  Future<Membership> updateMembership({
    required String companyId,
    required String membershipId,
    required UpdateMembershipRequest request,
  });

  Future<void> deleteMembership({
    required String companyId,
    required String membershipId,
  });
}
