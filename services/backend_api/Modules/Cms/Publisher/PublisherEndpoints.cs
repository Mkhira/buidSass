using BackendApi.Modules.Cms.Authorization;
using BackendApi.Modules.Cms.Primitives;
using BackendApi.Modules.Cms.Publisher.ArchiveContent;
using BackendApi.Modules.Cms.Publisher.PublishNow;
using BackendApi.Modules.Cms.Publisher.SchedulePublish;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Cms.Publisher;

public static class PublisherEndpoints
{
    public static IEndpointRouteBuilder MapPublisherBannerEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/banner-slots/{id:guid}/schedule-publish", ScheduleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });

        builder.MapPost("/banner-slots/{id:guid}/publish-now", PublishNowAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });

        builder.MapPost("/banner-slots/{id:guid}/archive", ArchiveAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });

        return builder;
    }

    private static async Task<IResult> ScheduleAsync(
        Guid id,
        HttpContext context,
        [FromServices] SchedulePublishBannerHandler handler,
        CancellationToken ct)
    {
        var auth = AssertPublisher(context);
        if (auth is { } problem) return problem;

        var actorId = CmsResponseFactory.ResolveActorId(context)!.Value;
        var actorRole = CmsResponseFactory.ResolveActorRole(context);

        var result = await handler.HandleAsync(id, actorId, actorRole, ct);
        return ToHttp(context, result);
    }

    private static async Task<IResult> PublishNowAsync(
        Guid id,
        HttpContext context,
        [FromServices] PublishNowBannerHandler handler,
        CancellationToken ct)
    {
        var auth = AssertPublisher(context);
        if (auth is { } problem) return problem;

        var actorId = CmsResponseFactory.ResolveActorId(context)!.Value;
        var actorRole = CmsResponseFactory.ResolveActorRole(context);

        var result = await handler.HandleAsync(id, actorId, actorRole, ct);
        return ToHttp(context, result);
    }

    private static async Task<IResult> ArchiveAsync(
        Guid id,
        [FromBody] ArchiveBannerRequest? body,
        HttpContext context,
        [FromServices] ArchiveBannerHandler handler,
        CancellationToken ct)
    {
        var auth = AssertPublisher(context);
        if (auth is { } problem) return problem;

        if (body is null)
        {
            return CmsResponseFactory.Problem(context, 400,
                CmsReasonCode.ArchiveReasonNoteRequired, "Request body is required.");
        }

        var actorId = CmsResponseFactory.ResolveActorId(context)!.Value;
        var actorRole = CmsResponseFactory.ResolveActorRole(context);

        var result = await handler.HandleAsync(id, actorId, actorRole, body, ct);
        return ToHttp(context, result);
    }

    private static IResult? AssertPublisher(HttpContext context)
    {
        if (!CmsResponseFactory.HasPermissionClaim(context, CmsPermissions.Publisher))
        {
            return CmsResponseFactory.Problem(context, 403,
                CmsReasonCode.PublishRoleRequired, "cms.publisher permission required.");
        }
        if (CmsResponseFactory.ResolveActorId(context) is null)
        {
            return CmsResponseFactory.Problem(context, 401,
                CmsReasonCode.PublishRoleRequired, "Authenticated actor required.");
        }
        return null;
    }

    private static IResult ToHttp(HttpContext context, PublisherResult result)
    {
        if (!result.IsSuccess)
        {
            return CmsResponseFactory.Problem(context, result.Status, result.ReasonCode!,
                "Banner publish operation rejected.", result.Detail, result.Extensions);
        }
        return Results.Ok(result.Row);
    }
}
