using BackendApi.Modules.Verification.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Verification.Admin.OpenHistoricalDocument;

public static class OpenHistoricalDocumentEndpoint
{
    public static IEndpointRouteBuilder MapOpenHistoricalDocumentEndpoint(
        this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id:guid}/documents/{documentId:guid}/open", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = AdminAuthorizationDefaults.AuthenticationScheme,
            });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        Guid documentId,
        HttpContext context,
        OpenHistoricalDocumentHandler handler,
        CancellationToken ct)
    {
        if (!context.User.HasClaim("permission", VerificationPermissions.Review)
         && !context.User.HasClaim("permissions", VerificationPermissions.Review))
        {
            return AdminVerificationResponseFactory.Problem(
                context, 403,
                "verification.review_permission_required",
                "verification.review permission required.");
        }

        var reviewerMarkets = AdminVerificationResponseFactory.ResolveAssignedMarkets(context);
        var result = await handler.HandleAsync(id, documentId, reviewerMarkets, ct);

        if (!result.Exists)
        {
            return AdminVerificationResponseFactory.Problem(
                context, 404,
                "verification.document_not_found",
                "Document not found.");
        }

        if (result.IsPurged)
        {
            return AdminVerificationResponseFactory.Problem(
                context, 410,
                "verification.document_purged",
                "Document body has been purged per the market's retention policy.",
                detail: null,
                extensions: new Dictionary<string, object?>
                {
                    ["purged_at"] = result.PurgedAt,
                    ["retention_policy_version"] = "v1",
                });
        }

        return Results.Ok(result.Response);
    }
}
