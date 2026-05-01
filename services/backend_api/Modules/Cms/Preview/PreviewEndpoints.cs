using BackendApi.Modules.Cms.Authorization;
using BackendApi.Modules.Cms.Preview.MintPreviewToken;
using BackendApi.Modules.Cms.Preview.ReadPreviewedDraft;
using BackendApi.Modules.Cms.Preview.RevokePreviewToken;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Cms.Preview;

public static class PreviewEndpoints
{
    public static IEndpointRouteBuilder MapPreviewAdminEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{kind}/{id:guid}/preview-token", MintAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        builder.MapDelete("/preview-token/{tokenHashHex}", RevokeAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    public static IEndpointRouteBuilder MapPreviewStorefrontEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/preview/{kind}/{id:guid}", ReadAsync).AllowAnonymous();
        return builder;
    }

    private static async Task<IResult> MintAsync(
        string kind,
        Guid id,
        [FromBody] MintPreviewTokenRequest? body,
        HttpContext context,
        [FromServices] MintPreviewTokenHandler handler,
        CancellationToken ct)
    {
        // Any of cms.editor / cms.publisher / cms.legal_owner / super_admin may mint.
        var allowed =
            CmsResponseFactory.HasPermissionClaim(context, CmsPermissions.Editor) ||
            CmsResponseFactory.HasPermissionClaim(context, CmsPermissions.Publisher) ||
            CmsResponseFactory.HasPermissionClaim(context, CmsPermissions.LegalOwner) ||
            CmsResponseFactory.HasPermissionClaim(context, "super_admin");
        if (!allowed)
        {
            return CmsResponseFactory.Problem(context, 403,
                CmsReasonCode.EditorRoleRequired,
                "cms.editor / cms.publisher / cms.legal_owner / super_admin permission required.");
        }
        var actorId = CmsResponseFactory.ResolveActorId(context);
        if (actorId is null)
        {
            return CmsResponseFactory.Problem(context, 401,
                CmsReasonCode.EditorRoleRequired, "Authenticated actor required.");
        }

        if (!TryParseEntityKind(kind, out var entityKind))
        {
            return CmsResponseFactory.Problem(context, 404,
                CmsReasonCode.PreviewEntityNotDraftable, $"Unsupported entity kind '{kind}'.");
        }

        var actorRole = CmsResponseFactory.ResolveActorRole(context);
        var result = await handler.HandleAsync(
            entityKind, id, actorId.Value, actorRole,
            body ?? new MintPreviewTokenRequest(null), ct);
        if (!result.IsSuccess)
        {
            return CmsResponseFactory.Problem(context, result.Status, result.ReasonCode!,
                "Preview token mint rejected.", result.Detail);
        }
        return Results.Created($"/v1/admin/cms/preview-token/{result.Response!.TokenHashHex}", result.Response);
    }

    private static async Task<IResult> RevokeAsync(
        string tokenHashHex,
        HttpContext context,
        [FromServices] RevokePreviewTokenHandler handler,
        CancellationToken ct)
    {
        var allowed =
            CmsResponseFactory.HasPermissionClaim(context, CmsPermissions.Editor) ||
            CmsResponseFactory.HasPermissionClaim(context, CmsPermissions.Publisher) ||
            CmsResponseFactory.HasPermissionClaim(context, CmsPermissions.LegalOwner) ||
            CmsResponseFactory.HasPermissionClaim(context, "super_admin");
        if (!allowed)
        {
            return CmsResponseFactory.Problem(context, 403,
                CmsReasonCode.EditorRoleRequired, "cms permission required.");
        }
        var actorId = CmsResponseFactory.ResolveActorId(context);
        if (actorId is null)
        {
            return CmsResponseFactory.Problem(context, 401,
                CmsReasonCode.EditorRoleRequired, "Authenticated actor required.");
        }

        byte[] hash;
        try
        {
            hash = Convert.FromHexString(tokenHashHex);
        }
        catch (FormatException)
        {
            return CmsResponseFactory.Problem(context, 400,
                CmsReasonCode.PreviewTokenSignatureInvalid, "token_hash must be hex-encoded.");
        }

        var actorRole = CmsResponseFactory.ResolveActorRole(context);
        var result = await handler.HandleAsync(hash, actorId.Value, actorRole, ct);
        if (!result.IsSuccess)
        {
            return CmsResponseFactory.Problem(context, result.Status, result.ReasonCode!,
                "Preview token revoke rejected.", result.Detail);
        }
        return Results.Ok(new { revoked_at_utc = result.RevokedAtUtc });
    }

    private static async Task<IResult> ReadAsync(
        string kind,
        Guid id,
        [FromQuery] string? token,
        HttpContext context,
        [FromServices] ReadPreviewedDraftHandler handler,
        CancellationToken ct)
    {
        if (!TryParseEntityKind(kind, out var entityKind))
        {
            return CmsResponseFactory.Problem(context, 404,
                CmsReasonCode.PreviewEntityNotDraftable, $"Unsupported entity kind '{kind}'.");
        }
        var result = await handler.HandleAsync(entityKind, id, token ?? string.Empty, ct);
        context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        if (!result.IsSuccess)
        {
            return CmsResponseFactory.Problem(context, result.Status, result.ReasonCode!,
                "Preview read rejected.", result.Detail);
        }
        return Results.Ok(result.Payload);
    }

    private static bool TryParseEntityKind(string wire, out EntityKind kind)
    {
        try
        {
            kind = EntityKindWire.FromWire(wire);
            return true;
        }
        catch (Exception)
        {
            kind = default;
            return false;
        }
    }
}
