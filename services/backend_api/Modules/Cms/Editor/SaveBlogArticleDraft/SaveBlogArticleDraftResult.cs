using BackendApi.Modules.Cms.Entities;

namespace BackendApi.Modules.Cms.Editor.SaveBlogArticleDraft;

public sealed record SaveBlogArticleDraftResult(
    bool IsSuccess,
    BlogArticle? Row,
    string? ReasonCode,
    string? Detail,
    int Status)
{
    public static SaveBlogArticleDraftResult Created(BlogArticle row) => new(true, row, null, null, 201);
    public static SaveBlogArticleDraftResult Updated(BlogArticle row) => new(true, row, null, null, 200);
    public static SaveBlogArticleDraftResult Reject(string reason, string detail, int status) =>
        new(false, null, reason, detail, status);
}
