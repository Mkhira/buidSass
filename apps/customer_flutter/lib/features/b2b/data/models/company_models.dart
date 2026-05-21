import 'package:flutter/foundation.dart';

// ============================================================
// Companies — Phase 8 customer surface
// ============================================================
// Models parse the wire shapes in `data-model.md` §Companies.

/// Wire role enum. Unknown roles default to `buyer` (the least
/// privileged role) so a forward-compat server-added role does not
/// surface admin-only actions to a client that doesn't recognize it.
const Set<String> kKnownCompanyRoles = {'admin', 'buyer', 'approver'};

@immutable
class Branch {
  const Branch({required this.id, required this.name, required this.address});
  final String id;
  final String name;
  final String address;

  factory Branch.fromJson(Map<String, Object?> j) => Branch(
        id: j['id'] as String? ?? j['branchId'] as String? ?? '',
        name: j['name'] as String? ?? '',
        address: j['address'] as String? ?? '',
      );
}

@immutable
class Membership {
  const Membership({
    required this.id,
    required this.userId,
    required this.name,
    required this.role,
  });

  final String id;
  final String userId;
  final String name;
  final String role;

  factory Membership.fromJson(Map<String, Object?> j) => Membership(
        id: j['id'] as String? ?? j['membershipId'] as String? ?? '',
        userId: j['userId'] as String? ?? '',
        name: j['name'] as String? ?? '',
        role: j['role'] as String? ?? 'buyer',
      );
}

@immutable
class Company {
  const Company({
    required this.id,
    required this.name,
    required this.vatNumber,
    required this.address,
    required this.marketCode,
    required this.myRole,
    required this.branches,
    required this.memberships,
    this.commercialRegistration,
  });

  final String id;
  final String name;
  final String vatNumber;
  final String address;
  final String? commercialRegistration;
  final String marketCode;
  final String myRole;
  final List<Branch> branches;
  final List<Membership> memberships;

  bool get isAdmin => myRole == 'admin';
  bool get isApprover => myRole == 'approver';

  factory Company.fromJson(Map<String, Object?> j) {
    final branches = j['branches'];
    final memberships = j['memberships'];
    return Company(
      id: j['id'] as String? ?? '',
      name: j['name'] as String? ?? '',
      vatNumber: j['vatNumber'] as String? ?? '',
      address: j['address'] as String? ?? '',
      commercialRegistration: j['commercialRegistration'] as String?,
      marketCode: j['marketCode'] as String? ?? '',
      myRole: j['myRole'] as String? ?? 'buyer',
      branches: branches is List
          ? branches
              .whereType<Map>()
              .map((m) => Branch.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      memberships: memberships is List
          ? memberships
              .whereType<Map>()
              .map((m) => Membership.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
    );
  }
}

@immutable
class CreateCompanyRequest {
  const CreateCompanyRequest({
    required this.name,
    required this.vatNumber,
    required this.address,
    required this.marketCode,
    this.commercialRegistration,
  });

  final String name;
  final String vatNumber;
  final String address;
  final String marketCode;
  final String? commercialRegistration;

  Map<String, Object?> toJson() => {
        'name': name,
        'vatNumber': vatNumber,
        'address': address,
        'marketCode': marketCode,
        if (commercialRegistration != null &&
            commercialRegistration!.isNotEmpty)
          'commercialRegistration': commercialRegistration,
      };
}

@immutable
class CreateCompanyResult {
  const CreateCompanyResult({
    required this.id,
    required this.name,
    required this.createdAt,
  });

  final String id;
  final String name;
  final DateTime createdAt;

  factory CreateCompanyResult.fromJson(Map<String, Object?> j) =>
      CreateCompanyResult(
        id: j['id'] as String? ?? '',
        name: j['name'] as String? ?? '',
        createdAt: DateTime.tryParse(j['createdAt'] as String? ?? '') ??
            DateTime.now(),
      );
}

@immutable
class UpdateCompanyRequest {
  const UpdateCompanyRequest({
    this.name,
    this.vatNumber,
    this.address,
    this.commercialRegistration,
  });

  final String? name;
  final String? vatNumber;
  final String? address;
  final String? commercialRegistration;

  Map<String, Object?> toJson() => {
        if (name != null) 'name': name,
        if (vatNumber != null) 'vatNumber': vatNumber,
        if (address != null) 'address': address,
        if (commercialRegistration != null)
          'commercialRegistration': commercialRegistration,
      };
}

@immutable
class CreateBranchRequest {
  const CreateBranchRequest({required this.name, required this.address});
  final String name;
  final String address;

  Map<String, Object?> toJson() => {'name': name, 'address': address};
}

@immutable
class CreateInvitationRequest {
  const CreateInvitationRequest({required this.email, required this.role});
  final String email;
  final String role;

  Map<String, Object?> toJson() => {'email': email, 'role': role};
}

@immutable
class CreateInvitationResult {
  const CreateInvitationResult({
    required this.invitationId,
    required this.email,
    required this.role,
    required this.sentAt,
  });

  final String invitationId;
  final String email;
  final String role;
  final DateTime sentAt;

  factory CreateInvitationResult.fromJson(Map<String, Object?> j) =>
      CreateInvitationResult(
        invitationId: j['invitationId'] as String? ?? '',
        email: j['email'] as String? ?? '',
        role: j['role'] as String? ?? 'buyer',
        sentAt:
            DateTime.tryParse(j['sentAt'] as String? ?? '') ?? DateTime.now(),
      );
}

@immutable
class AcceptInvitationResult {
  const AcceptInvitationResult({required this.companyId, required this.role});
  final String companyId;
  final String role;

  factory AcceptInvitationResult.fromJson(Map<String, Object?> j) =>
      AcceptInvitationResult(
        companyId: j['companyId'] as String? ?? '',
        role: j['role'] as String? ?? 'buyer',
      );
}

@immutable
class UpdateMembershipRequest {
  const UpdateMembershipRequest({required this.role});
  final String role;

  Map<String, Object?> toJson() => {'role': role};
}
