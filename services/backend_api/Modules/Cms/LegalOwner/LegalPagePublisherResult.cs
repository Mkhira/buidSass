using BackendApi.Modules.Cms.Entities;

namespace BackendApi.Modules.Cms.LegalOwner;

/// <summary>
/// Outcome of a legal-page publish/schedule slice. Mirrors
/// <c>Publisher.PublisherResult</c>; carries optional supersession context so
/// callers can render the prior-live → superseded transition that ran in the
/// same DB transaction.
/// </summary>
public sealed record LegalPagePublisherResult(
    bool IsSuccess,
    LegalPageVersion? Row,
    LegalPageVersion? SupersededRow,
    string? ReasonCode,
    string? Detail,
    int Status,
    IDictionary<string, object?>? Extensions = null)
{
    public static LegalPagePublisherResult Ok(
        LegalPageVersion row,
        LegalPageVersion? supersededRow = null) =>
        new(true, row, supersededRow, null, null, 200);

    public static LegalPagePublisherResult Reject(
        string reasonCode,
        string detail,
        int status,
        IDictionary<string, object?>? extensions = null) =>
        new(false, null, null, reasonCode, detail, status, extensions);
}
