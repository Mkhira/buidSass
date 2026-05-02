using BackendApi.Modules.Cms.Entities;

namespace BackendApi.Modules.Cms.Editor.SaveBannerDraft;

public sealed record SaveBannerDraftResult(
    bool IsSuccess,
    BannerSlot? Row,
    string? ReasonCode,
    string? Detail,
    int Status)
{
    public static SaveBannerDraftResult Created(BannerSlot row) => new(true, row, null, null, 201);
    public static SaveBannerDraftResult Updated(BannerSlot row) => new(true, row, null, null, 200);
    public static SaveBannerDraftResult Reject(string reason, string detail, int status) =>
        new(false, null, reason, detail, status);
}
