namespace BackendApi.Modules.Cms.Authorization;

/// <summary>
/// Permission constants used by <c>[RequirePermission(...)]</c> attribute
/// decorators on the CMS slice endpoints. Spec 015 binds these to roles.
/// Per spec 024 contract §1.
/// </summary>
public static class CmsPermissions
{
    public const string Editor = "cms.editor";
    public const string Publisher = "cms.publisher";
    public const string LegalOwner = "cms.legal_owner";
    public const string SuperAdmin = "cms.super_admin";
    public const string ViewerFinance = "cms.viewer.finance";
    public const string ViewerB2b = "cms.viewer.b2b";
}
