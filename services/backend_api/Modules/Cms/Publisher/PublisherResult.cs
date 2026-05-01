using BackendApi.Modules.Cms.Entities;

namespace BackendApi.Modules.Cms.Publisher;

public sealed record PublisherResult(
    bool IsSuccess,
    BannerSlot? Row,
    string? ReasonCode,
    string? Detail,
    int Status,
    IDictionary<string, object?>? Extensions = null)
{
    public static PublisherResult Ok(BannerSlot row) => new(true, row, null, null, 200);
    public static PublisherResult Reject(string reason, string detail, int status,
        IDictionary<string, object?>? extensions = null) =>
        new(false, null, reason, detail, status, extensions);
}
