using System.Text.Json;
using System.Text.Json.Serialization;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.Quotes;
using BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromCart;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackendApi.Modules.B2B.Quotes.Customer.SaveAsRepeatOrderTemplate;

/// <summary>
/// Spec 021 contract §2.9 — save an <c>accepted</c> quote as a named repeat-order
/// template. Uniqueness enforced via partial indexes (research §R12); concurrent
/// inserts of the same name surface <c>409 template.name_already_exists</c>.
/// </summary>
public sealed record SaveAsRepeatOrderTemplateRequest(
    [property: JsonPropertyName("name")] LocalizedMessage? Name);

public sealed record SaveAsRepeatOrderTemplateResponse(
    [property: JsonPropertyName("id")] Guid Id);

public sealed class SaveAsRepeatOrderTemplateValidator : AbstractValidator<SaveAsRepeatOrderTemplateRequest>
{
    public SaveAsRepeatOrderTemplateValidator()
    {
        RuleFor(x => x.Name)
            .Must(n => n is not null && (!string.IsNullOrWhiteSpace(n.En) || !string.IsNullOrWhiteSpace(n.Ar)))
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage("name must include at least one of {en, ar}");
    }
}

public sealed class SaveAsRepeatOrderTemplateHandler
{
    private readonly B2BDbContext _db;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _time;
    private readonly Microsoft.Extensions.Logging.ILogger<SaveAsRepeatOrderTemplateHandler> _logger;

    public SaveAsRepeatOrderTemplateHandler(
        B2BDbContext db,
        IAuditEventPublisher audit,
        TimeProvider time,
        Microsoft.Extensions.Logging.ILogger<SaveAsRepeatOrderTemplateHandler> logger)
    { _db = db; _audit = audit; _time = time; _logger = logger; }

    public async Task<SaveResult> HandleAsync(
        Guid actorId,
        Guid quoteId,
        SaveAsRepeatOrderTemplateRequest req,
        CancellationToken ct)
    {
        var quote = await _db.Quotes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == quoteId, ct);
        if (quote is null) return SaveResult.NotFound();

        // Visibility: customer-owner OR membership. We capture the actual access
        // role here so the audit event reflects the real authority used (CodeRabbit
        // Round 2: was hard-coded to "buyer" for company quotes regardless of the
        // membership role that granted access).
        string accessRole = quote.CustomerId == actorId ? "customer" : "unknown";
        var allowed = quote.CustomerId == actorId;
        if (!allowed && quote.CompanyId is { } cid)
        {
            var roles = await _db.CompanyMemberships.AsNoTracking()
                .Where(m => m.CompanyId == cid && m.UserId == actorId
                    && (m.Role == "buyer" || m.Role == "companies.admin"))
                .Select(m => m.Role)
                .ToListAsync(ct);
            if (roles.Count > 0)
            {
                allowed = true;
                // Prefer companies.admin attribution when both roles are held.
                accessRole = roles.Contains("companies.admin") ? "companies.admin" : "buyer";
            }
        }
        if (!allowed) return SaveResult.NotFound();

        if (quote.State != "accepted") return SaveResult.InvalidState();

        var nameJson = JsonSerializer.Serialize(new { en = req.Name?.En ?? "", ar = req.Name?.Ar ?? "" });
        var entity = new RepeatOrderTemplate
        {
            Id = Guid.NewGuid(),
            MarketCode = quote.MarketCode,
            SourceQuoteId = quote.Id,
            CompanyId = quote.CompanyId,
            UserId = actorId,
            NameJson = nameJson,
            CreatedAt = _time.GetUtcNow(),
            CreatedBy = actorId,
        };
        _db.RepeatOrderTemplates.Add(entity);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (IsUnique(ex))
        {
            return SaveResult.NameAlreadyExists();
        }

        // CodeRabbit Round 1 — Principle 25: template creation is a structural
        // change to customer/company data and is auditable.
        // CodeRabbit Round 2: ActorRole reflects the authority that actually granted
        // access (resolved above); audit publishing failures are logged so silent
        // compliance gaps are visible to ops.
        try
        {
            await _audit.PublishAsync(new AuditEvent(
                ActorId: actorId, ActorRole: accessRole,
                Action: "quote.repeat_order_template_saved",
                EntityType: "repeat_order_template", EntityId: entity.Id,
                BeforeState: null,
                AfterState: new { source_quote_id = quote.Id, company_id = quote.CompanyId, market_code = quote.MarketCode },
                Reason: null), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SaveAsRepeatOrderTemplate: failed to publish quote.repeat_order_template_saved "
                + "audit event (template_id={TemplateId}, source_quote_id={QuoteId}). "
                + "Audit-pipeline replay required.",
                entity.Id, quote.Id);
        }

        return SaveResult.Success(entity.Id);
    }

    private static bool IsUnique(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;
}

public sealed record SaveResult(
    bool IsSuccess, int StatusCode, QuoteReasonCode? ReasonCode, Guid? Id)
{
    public static SaveResult Success(Guid id) => new(true, 201, null, id);
    public static SaveResult NotFound() => new(false, 404, QuoteReasonCode.QuoteNotFound, null);
    public static SaveResult InvalidState() => new(false, 409, QuoteReasonCode.QuoteInvalidStateForAction, null);
    public static SaveResult NameAlreadyExists() => new(false, 409, QuoteReasonCode.TemplateNameAlreadyExists, null);
}

public static class SaveAsRepeatOrderTemplateEndpoint
{
    public static IEndpointRouteBuilder MapSaveAsRepeatOrderTemplateEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/save-as-template", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        [FromBody] SaveAsRepeatOrderTemplateRequest? body,
        HttpContext context,
        SaveAsRepeatOrderTemplateHandler handler,
        IValidator<SaveAsRepeatOrderTemplateRequest> validator,
        CancellationToken ct)
    {
        var actorId = B2BResponseFactory.ResolveCustomerId(context);
        if (actorId is null)
        {
            return B2BResponseFactory.Problem(context, 401,
                QuoteReasonCode.QuoteRequiredFieldMissing,
                "Authentication required.");
        }
        body ??= new SaveAsRepeatOrderTemplateRequest(null);
        var v = await validator.ValidateAsync(body, ct);
        if (!v.IsValid)
        {
            var first = v.Errors[0];
            return Results.Json(new
            {
                type = $"https://errors.dental-commerce/quotes/{first.ErrorCode}",
                title = "Template validation failed.",
                status = 400,
                detail = string.Join("; ", v.Errors.Select(e => e.ErrorMessage)),
                instance = context.Request.Path.ToString(),
                reasonCode = first.ErrorCode,
            }, statusCode: 400, contentType: "application/problem+json");
        }
        var r = await handler.HandleAsync(actorId.Value, id, body, ct);
        if (r.IsSuccess) return Results.Json(new SaveAsRepeatOrderTemplateResponse(r.Id!.Value), statusCode: 201);
        return B2BResponseFactory.Problem(context, r.StatusCode, r.ReasonCode!.Value, "Save-as-template rejected.");
    }
}
