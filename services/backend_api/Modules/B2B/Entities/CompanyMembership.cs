namespace BackendApi.Modules.B2B.Entities;

/// <summary>
/// Spec 021 company-membership row (data-model §2.2). Binds an Identity user to a
/// <see cref="Company"/> in one of three roles: <c>companies.admin</c>, <c>buyer</c>,
/// or <c>approver</c>.
///
/// Invariants enforced at the handler layer (covered by tests):
/// <list type="bullet">
///   <item>A company MUST always have ≥ 1 <c>companies.admin</c> (FR-024).</item>
///   <item>When the company has <c>approver_required=true</c>, MUST have ≥ 1 <c>approver</c> (FR-025).</item>
/// </list>
/// </summary>
public sealed class CompanyMembership
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }

    /// <summary>Two-letter market code (mirrors the parent <c>Company.MarketCode</c>); ADR-010 partitioning.</summary>
    public string MarketCode { get; set; } = string.Empty;

    /// <summary>Logical FK to spec 004's customer/user identity table.</summary>
    public Guid UserId { get; set; }

    /// <summary>One of <c>companies.admin | buyer | approver</c>.</summary>
    public string Role { get; set; } = string.Empty;

    public DateTimeOffset JoinedAt { get; set; }
}
