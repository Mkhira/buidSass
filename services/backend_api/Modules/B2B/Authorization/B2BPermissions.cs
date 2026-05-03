namespace BackendApi.Modules.B2B.Authorization;

/// <summary>
/// Spec 021 admin-side permission constants (research §R9). Consumed via
/// <c>[RequirePermission(...)]</c> attributes on admin endpoints (Phase 3+).
///
/// Customer-side roles (<c>buyer</c>, <c>approver</c>, <c>companies.admin</c> membership)
/// are NOT in this list — they are derived from <see cref="Entities.CompanyMembership"/>
/// rows + identity claims, not from the platform RBAC system. The membership-driven
/// authorization is intentional: mixing customer-side membership into admin RBAC would
/// force every membership change through admin tooling.
/// </summary>
public static class B2BPermissions
{
    /// <summary>Admin authoring + revisions + publish on quotes (admin queue).</summary>
    public const string QuotesAuthor = "quotes.author";

    /// <summary>Read-only across every quote (admin support / finance scenarios).</summary>
    public const string QuotesReview = "quotes.review";

    /// <summary>Admin company-suspend action — declared here, granted by spec 019's role model.</summary>
    public const string CompaniesSuspend = "companies.suspend";

    /// <summary>
    /// Admin company-moderation surface — declared here, granted by spec 019. Distinct
    /// from the customer-side <c>companies.admin</c> membership role (see research §R9).
    /// </summary>
    public const string CompaniesAdmin = "companies.admin";

    /// <summary>Admin read access to company directory; declared here, granted by spec 019.</summary>
    public const string CompaniesRead = "companies.read";
}
