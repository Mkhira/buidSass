using BackendApi.Modules.Cms.Entities;

namespace BackendApi.Modules.Cms.LegalOwner.SaveLegalPageVersionDraft;

public sealed record SaveLegalPageVersionDraftResult(
    bool IsSuccess,
    LegalPageVersion? Row,
    string? ReasonCode,
    string? Detail,
    int Status,
    IDictionary<string, object?>? Extensions = null)
{
    public static SaveLegalPageVersionDraftResult Created(LegalPageVersion row) =>
        new(true, row, null, null, 201);

    public static SaveLegalPageVersionDraftResult Updated(LegalPageVersion row) =>
        new(true, row, null, null, 200);

    public static SaveLegalPageVersionDraftResult Reject(
        string reasonCode,
        string detail,
        int status,
        IDictionary<string, object?>? extensions = null) =>
        new(false, null, reasonCode, detail, status, extensions);
}
