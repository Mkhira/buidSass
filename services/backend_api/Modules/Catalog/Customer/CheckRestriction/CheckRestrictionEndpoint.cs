using BackendApi.Modules.Catalog.Customer.Common;
using BackendApi.Modules.Catalog.Primitives.Restriction;
using FluentValidation;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Catalog.Customer.CheckRestriction;

public static class CheckRestrictionEndpoint
{
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("/restrictions/check", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        CheckRestrictionRequest request,
        RestrictionEvaluator evaluator,
        CancellationToken cancellationToken)
    {
        var validator = new CheckRestrictionRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CustomerCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "catalog.restrictions.invalid_request",
                "Invalid restriction check request",
                validation.Errors.First().ErrorMessage);
        }

        var decision = await evaluator.CheckAsync(
            request.ProductId,
            request.MarketCode,
            request.VerificationState,
            cancellationToken);

        return Results.Ok(new CheckRestrictionResponse(decision.Allowed, decision.ReasonCode));
    }
}

public sealed record CheckRestrictionRequest(Guid ProductId, string MarketCode, string VerificationState);

public sealed class CheckRestrictionRequestValidator : AbstractValidator<CheckRestrictionRequest>
{
    public CheckRestrictionRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.MarketCode).NotEmpty().MaximumLength(16);
        RuleFor(x => x.VerificationState)
            .NotEmpty()
            .Must(v => v is "unverified" or "pending" or "verified" or "rejected" or "expired")
            .WithMessage("verificationState must be unverified|pending|verified|rejected|expired");
    }
}

public sealed record CheckRestrictionResponse(bool Allowed, string ReasonCode);
