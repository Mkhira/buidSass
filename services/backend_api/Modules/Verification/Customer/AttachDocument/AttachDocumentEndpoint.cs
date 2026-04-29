using BackendApi.Modules.Verification.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Verification.Customer.AttachDocument;

public static class AttachDocumentEndpoint
{
    public static IEndpointRouteBuilder MapAttachDocumentEndpoint(
        this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/documents", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        AttachDocumentRequest? body,
        HttpContext context,
        AttachDocumentHandler handler,
        CancellationToken ct)
    {
        var customerId = VerificationResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return VerificationResponseFactory.Problem(
                context, 401,
                VerificationReasonCode.AccountInactive,
                "Authentication required.");
        }

        if (body is null
            || string.IsNullOrWhiteSpace(body.StorageKey)
            || string.IsNullOrWhiteSpace(body.ContentType))
        {
            return VerificationResponseFactory.Problem(
                context, 400,
                VerificationReasonCode.RequiredFieldMissing,
                "storage_key and content_type are required.");
        }

        if (string.IsNullOrWhiteSpace(context.Request.Headers["Idempotency-Key"].ToString()))
        {
            return VerificationResponseFactory.Problem(
                context, 400,
                VerificationReasonCode.IdempotencyKeyMissing,
                "Idempotency-Key header is required for this endpoint.");
        }

        var result = await handler.HandleAsync(customerId.Value, id, body, ct);
        if (result.IsNotFound)
        {
            return VerificationResponseFactory.Problem(
                context, 404,
                "verification.not_found",
                "Verification not found.");
        }
        if (!result.IsSuccess)
        {
            var status = result.ReasonCode switch
            {
                VerificationReasonCode.InvalidStateForAction => 409,
                VerificationReasonCode.DocumentScanInfected => 400,
                VerificationReasonCode.DocumentScanPending => 409,
                _ => 400,
            };
            return VerificationResponseFactory.Problem(
                context, status, result.ReasonCode!.Value, "Attach failed.", result.Detail);
        }
        return Results.Created(
            $"/api/customer/verifications/{id}/documents/{result.Response!.DocumentId}",
            result.Response);
    }
}
