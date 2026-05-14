using BackendApi.Features.Seeding;
using BackendApi.Modules.Shipping.Features.Methods;
using BackendApi.Modules.Shipping.Features.Zones;
using BackendApi.Modules.Shipping.Quote;
using BackendApi.Modules.Shipping.Seeding;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BackendApi.Modules.Shipping;

/// <summary>
/// Slice + endpoint registrations for the Shipping module. Each partial
/// hangs off <see cref="ShippingModule"/> so adding a phase = appending
/// one method here, not touching <c>ShippingModule.cs</c>.
/// </summary>
public static partial class ShippingModule
{
    static partial void AddSlices(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        // MediatR — idempotent across modules; appends our assembly's handlers.
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<QuoteShippingHandler>());

        // Phase 2 — quote + zones.
        services.AddScoped<ZoneResolver>();
        services.AddScoped<QuoteShippingHandler>();
        services.AddScoped<CreateDraftMethodHandler>();
        services.AddScoped<UpdateFeeTableHandler>();
        services.AddScoped<CreateZoneHandler>();

        // Phase 4 — method lifecycle (submit / approve / reject / archive).
        services.AddScoped<SubmitForReviewHandler>();
        services.AddScoped<ApproveMethodHandler>();
        services.AddScoped<RejectMethodHandler>();
        services.AddScoped<ArchiveMethodHandler>();

        // Phase 5 — shipment actions + routing + workers.
        AddPhase5(services);

        // Phase 7 — reference-data seeding (idempotent in all environments).
        services.AddScoped<ISeeder, ShippingV1Seeder>();
    }

    static partial void MapCustomerEndpoints(IEndpointRouteBuilder customer)
    {
        // POST /v1/shipping/quote — AC-4, AC-5.
        customer.MapPost("/quote", async (
            [FromBody] QuoteRequest body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            if (body is null)
            {
                return Results.BadRequest(new { error = "request_body_required" });
            }
            var result = await mediator.Send(new QuoteShippingCommand(
                MarketCode: body.MarketCode,
                City: body.City,
                PostalCode: body.PostalCode,
                WeightKg: body.WeightKg,
                CartTotal: body.CartTotal,
                CustomerTier: body.CustomerTier), ct);

            if (!result.ZoneResolved)
            {
                // AC-5 — out-of-zone returns 200 with empty methods and a reason code.
                return Results.Ok(new QuoteResponse(
                    ZoneResolved: false,
                    ZoneId: null,
                    Methods: Array.Empty<QuoteResponseMethod>(),
                    Reason: "shipping_not_available_to_this_address"));
            }
            return Results.Ok(new QuoteResponse(
                ZoneResolved: true,
                ZoneId: result.ZoneId,
                Methods: result.Methods.Select(m => new QuoteResponseMethod(
                    MethodId: m.MethodId,
                    MethodVersionId: m.MethodVersionId,
                    NameAr: m.NameAr,
                    NameEn: m.NameEn,
                    FeeAmount: m.FeeAmount,
                    Currency: m.Currency,
                    EtaMinHours: m.EtaMinHours,
                    EtaMaxHours: m.EtaMaxHours)).ToArray(),
                Reason: null));
        })
        .AllowAnonymous()
        .WithName("shipping_quote")
        .WithTags("shipping");
    }

    static partial void MapAdminEndpoints(IEndpointRouteBuilder admin)
    {
        // POST /v1/admin/shipping/methods — author a draft.
        admin.MapPost("/methods", async (
            [FromBody] CreateDraftMethodBody body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(new CreateDraftMethodCommand(
                MarketCode: body.MarketCode,
                NameAr: body.NameAr,
                NameEn: body.NameEn,
                DescriptionAr: body.DescriptionAr,
                DescriptionEn: body.DescriptionEn,
                EtaMinHours: body.EtaMinHours,
                EtaMaxHours: body.EtaMaxHours,
                AuthorId: body.AuthorId,
                EligibilityJson: body.EligibilityJson ?? "{}"), ct);
            return Results.Created($"/v1/admin/shipping/methods/{result.MethodId}", result);
        })
        .WithName("shipping_admin_create_method_draft")
        .WithTags("shipping-admin");

        // POST /v1/admin/shipping/methods/{id}/fee-tables — append fee tiers.
        admin.MapPost("/methods/{methodVersionId:guid}/fee-tables", async (
            Guid methodVersionId,
            [FromBody] UpdateFeeTableBody body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            var tiers = (body.Tiers ?? Array.Empty<FeeTierBody>())
                .Select(t => new FeeTier(t.WeightMinKg, t.WeightMaxKg, t.FeeAmount))
                .ToArray();
            var result = await mediator.Send(new UpdateFeeTableCommand(
                MethodVersionId: methodVersionId,
                ZoneId: body.ZoneId,
                Tiers: tiers,
                EffectiveAt: body.EffectiveAt), ct);
            return Results.Ok(result);
        })
        .WithName("shipping_admin_update_fee_table")
        .WithTags("shipping-admin");

        // POST /v1/admin/shipping/zones — create a zone.
        admin.MapPost("/zones", async (
            [FromBody] CreateZoneBody body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(new CreateZoneCommand(
                MarketCode: body.MarketCode,
                NameAr: body.NameAr,
                NameEn: body.NameEn,
                PostalCodePrefixes: body.PostalCodePrefixes ?? Array.Empty<string>(),
                CityList: body.CityList ?? Array.Empty<string>()), ct);
            return Results.Created($"/v1/admin/shipping/zones/{result.ZoneId}", result);
        })
        .WithName("shipping_admin_create_zone")
        .WithTags("shipping-admin");

        // Phase 4 — method lifecycle verbs.
        admin.MapPost("/method-versions/{id:guid}/submit", async (
            Guid id, [FromBody] LifecycleBody body,
            [FromServices] IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new SubmitForReviewCommand(id, body.ActorId), ct);
            return Results.NoContent();
        })
        .WithName("shipping_admin_submit_method")
        .WithTags("shipping-admin");

        admin.MapPost("/method-versions/{id:guid}/approve", async (
            Guid id, [FromBody] LifecycleBody body,
            [FromServices] IMediator mediator, CancellationToken ct) =>
        {
            try
            {
                await mediator.Send(new ApproveMethodCommand(id, body.ActorId), ct);
                return Results.NoContent();
            }
            catch (ShippingPublishGateException ex)
            {
                // Typed exception → no need to parse `ex.Message` to detect the
                // V-1 publish-gate failure.
                return Results.BadRequest(new { error = "publish_gate_failed", detail = ex.Message });
            }
        })
        .WithName("shipping_admin_approve_method")
        .WithTags("shipping-admin");

        admin.MapPost("/method-versions/{id:guid}/reject", async (
            Guid id, [FromBody] RejectBody body,
            [FromServices] IMediator mediator, CancellationToken ct) =>
        {
            try
            {
                await mediator.Send(new RejectMethodCommand(id, body.ActorId, body.Reason ?? "rejected"), ct);
                return Results.NoContent();
            }
            catch (ShippingPublishGateException ex)
            {
                return Results.BadRequest(new { error = "publish_gate_failed", detail = ex.Message });
            }
        })
        .WithName("shipping_admin_reject_method")
        .WithTags("shipping-admin");

        admin.MapPost("/method-versions/{id:guid}/archive", async (
            Guid id, [FromBody] LifecycleBody body,
            [FromServices] IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new ArchiveMethodCommand(id, body.ActorId), ct);
            return Results.NoContent();
        })
        .WithName("shipping_admin_archive_method")
        .WithTags("shipping-admin");

        // Phase 5 — shipment actions + provider routing.
        MapPhase5AdminEndpoints(admin);
    }
}

// --- HTTP request/response DTOs ---
// Kept inside the slices file so we never reach across slice boundaries
// to pick them up. JSON property names follow .NET convention; the
// OpenAPI doc generator handles case-conversion.

public sealed record QuoteRequest(
    string MarketCode,
    string? City,
    string? PostalCode,
    decimal WeightKg,
    decimal CartTotal,
    string? CustomerTier);

public sealed record QuoteResponse(
    bool ZoneResolved,
    Guid? ZoneId,
    IReadOnlyList<QuoteResponseMethod> Methods,
    string? Reason);

public sealed record QuoteResponseMethod(
    Guid MethodId,
    Guid MethodVersionId,
    string NameAr,
    string NameEn,
    decimal FeeAmount,
    string Currency,
    int EtaMinHours,
    int EtaMaxHours);

public sealed record CreateDraftMethodBody(
    string MarketCode,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    int EtaMinHours,
    int EtaMaxHours,
    Guid AuthorId,
    string? EligibilityJson);

public sealed record UpdateFeeTableBody(
    Guid ZoneId,
    FeeTierBody[]? Tiers,
    DateTimeOffset EffectiveAt);

public sealed record FeeTierBody(decimal WeightMinKg, decimal WeightMaxKg, decimal FeeAmount);

public sealed record CreateZoneBody(
    string MarketCode,
    string NameAr,
    string NameEn,
    string[]? PostalCodePrefixes,
    string[]? CityList);

public sealed record LifecycleBody(Guid ActorId);
public sealed record RejectBody(Guid ActorId, string? Reason);
