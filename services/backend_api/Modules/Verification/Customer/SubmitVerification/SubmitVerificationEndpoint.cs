using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Verification.Customer.SubmitVerification;

/// <summary>
/// HTTP surface for <see cref="SubmitVerificationHandler"/>. Wired by
/// <see cref="VerificationModule.MapVerificationEndpoints"/> at module bootstrap.
/// Requires the <c>Idempotency-Key</c> header per spec 020 contracts §2.1.
///
/// <para><b>Idempotency status (V1 partial):</b> the header is required and
/// forwarded to the handler, which stamps the key on the initial state-
/// transition row's metadata jsonb for audit-trail traceability. Full
/// <c>IdempotencyStore</c> semantics — body fingerprint, cached response
/// replay across instances, <c>409 idempotency_key_conflict</c> on body
/// mismatch — match the Checkout module's pattern and ship in a follow-up
/// (tracked: spec 003 platform middleware integration). Until then, naive
/// duplicate POSTs in quick succession will create separate verification
/// rows; the AlreadyPending guard in the handler catches the most-common
/// retry case (second submission while first is non-terminal returns 409).</para>
/// </summary>
public static class SubmitVerificationEndpoint
{
    public static IEndpointRouteBuilder MapSubmitVerificationEndpoint(
        this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        SubmitVerificationRequest? body,
        HttpContext context,
        SubmitVerificationHandler handler,
        CancellationToken ct)
    {
        var customerId = VerificationResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return VerificationResponseFactory.Problem(
                context, 401,
                Primitives.VerificationReasonCode.AccountInactive,
                "Authentication required.");
        }

        var (ok, reason, detail) = SubmitVerificationValidator.Validate(body);
        if (!ok)
        {
            return VerificationResponseFactory.Problem(
                context, 400,
                reason!.Value,
                "Submission validation failed.",
                detail);
        }

        var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return VerificationResponseFactory.Problem(
                context, 400,
                Primitives.VerificationReasonCode.IdempotencyKeyMissing,
                "Idempotency-Key header is required for this endpoint.");
        }

        var marketCode = VerificationResponseFactory.ResolveMarketCode(context);
        var result = await handler.HandleAsync(customerId.Value, marketCode, body!, idempotencyKey, ct);

        if (!result.IsSuccess)
        {
            var status = result.ReasonCode switch
            {
                Primitives.VerificationReasonCode.AlreadyPending => 409,
                Primitives.VerificationReasonCode.MarketUnsupported => 400,
                _ => 400,
            };
            return VerificationResponseFactory.Problem(
                context, status,
                result.ReasonCode!.Value,
                "Submission rejected.",
                result.Detail);
        }

        return Results.Created(
            $"/api/customer/verifications/{result.Response!.Id}",
            result.Response);
    }
}
