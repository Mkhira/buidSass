using BackendApi.Modules.Cms.Authorization;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Cms.Publisher.PublishNow;

public static class PublishNowBlogArticleEndpoint
{
    public static IEndpointRouteBuilder MapPublishNowBlogArticleEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/blog-articles/{id:guid}/publish-now", PublishNowAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        builder.MapPost("/blog-articles/{id:guid}/schedule-publish", SchedulePublishAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static Task<IResult> PublishNowAsync(
        Guid id, HttpContext context,
        [FromServices] PublishNowBlogArticleHandler handler,
        CancellationToken ct) => Core(id, schedule: false, context, handler, ct);

    private static Task<IResult> SchedulePublishAsync(
        Guid id, HttpContext context,
        [FromServices] PublishNowBlogArticleHandler handler,
        CancellationToken ct) => Core(id, schedule: true, context, handler, ct);

    private static async Task<IResult> Core(
        Guid id,
        bool schedule,
        HttpContext context,
        PublishNowBlogArticleHandler handler,
        CancellationToken ct)
    {
        if (!CmsResponseFactory.HasPermissionClaim(context, CmsPermissions.Publisher))
        {
            return CmsResponseFactory.Problem(context, 403,
                CmsReasonCode.PublishRoleRequired, "cms.publisher permission required.");
        }
        var actorId = CmsResponseFactory.ResolveActorId(context);
        if (actorId is null)
        {
            return CmsResponseFactory.Problem(context, 401,
                CmsReasonCode.PublishRoleRequired, "Authenticated actor required.");
        }
        var actorRole = CmsResponseFactory.ResolveActorRole(context);
        BlogPublisherResult result = await handler.HandleAsync(id, actorId.Value, actorRole, schedule, ct);
        if (!result.IsSuccess)
        {
            return CmsResponseFactory.Problem(context, result.Status, result.ReasonCode!,
                "Blog article publish rejected.", result.Detail, result.Extensions);
        }
        return Results.Ok(result.Row);
    }
}
