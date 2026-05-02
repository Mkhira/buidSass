using BackendApi.Modules.Cms.Authorization;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Cms.Publisher;

public static class PublishContentEndpoints
{
    public static IEndpointRouteBuilder MapPublishContentEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/featured-sections/{id:guid}/publish-now",
            (Guid id, HttpContext c, [FromServices] PublishContentSlice s, CancellationToken ct) =>
                Core(id, c, s, ct, schedule: false, kind: "featured"))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        builder.MapPost("/featured-sections/{id:guid}/schedule-publish",
            (Guid id, HttpContext c, [FromServices] PublishContentSlice s, CancellationToken ct) =>
                Core(id, c, s, ct, schedule: true, kind: "featured"))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        builder.MapPost("/faq-entries/{id:guid}/publish-now",
            (Guid id, HttpContext c, [FromServices] PublishContentSlice s, CancellationToken ct) =>
                Core(id, c, s, ct, schedule: false, kind: "faq"))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        builder.MapPost("/faq-entries/{id:guid}/schedule-publish",
            (Guid id, HttpContext c, [FromServices] PublishContentSlice s, CancellationToken ct) =>
                Core(id, c, s, ct, schedule: true, kind: "faq"))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> Core(
        Guid id,
        HttpContext context,
        PublishContentSlice slice,
        CancellationToken ct,
        bool schedule,
        string kind)
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
        var result = kind == "featured"
            ? await slice.PublishFeaturedSectionNowAsync(id, actorId.Value, actorRole, schedule, ct)
            : await slice.PublishFaqEntryNowAsync(id, actorId.Value, actorRole, schedule, ct);
        if (!result.IsSuccess)
        {
            return CmsResponseFactory.Problem(context, result.Status, result.ReasonCode!,
                $"{kind} publish rejected.", result.Detail, result.Extensions);
        }
        return Results.Ok(result.Row);
    }
}
