using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Catalog.Admin.Common;
using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Catalog.Primitives;
using BackendApi.Modules.Catalog.Primitives.Outbox;
using BackendApi.Modules.Catalog.Primitives.Restriction;
using BackendApi.Modules.Catalog.Primitives.StateMachines;
using BackendApi.Modules.Identity.Authorization.Filters;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Catalog.Admin.Products;

public static class ProductAdminEndpoints
{
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder builder)
    {
        var authorize = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };

        builder.MapPost("/products", CreateAsync).RequireAuthorization(authorize).RequirePermission("catalog.product.write");
        builder.MapPatch("/products/{id:guid}", UpdateAsync).RequireAuthorization(authorize).RequirePermission("catalog.product.write");
        builder.MapPost("/products/{id:guid}/submit-for-review", SubmitAsync).RequireAuthorization(authorize).RequirePermission("catalog.product.submit");
        builder.MapPost("/products/{id:guid}/publish", PublishAsync).RequireAuthorization(authorize).RequirePermission("catalog.product.publish");
        builder.MapPost("/products/{id:guid}/cancel-schedule", CancelScheduleAsync).RequireAuthorization(authorize).RequirePermission("catalog.product.publish");
        builder.MapPost("/products/{id:guid}/archive", ArchiveAsync).RequireAuthorization(authorize).RequirePermission("catalog.product.archive");
        builder.MapGet("/products", ListAsync).RequireAuthorization(authorize).RequirePermission("catalog.product.write");
        builder.MapGet("/products/{id:guid}", GetOneAsync).RequireAuthorization(authorize).RequirePermission("catalog.product.write");

        return builder;
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context,
        CreateProductRequest request,
        CatalogDbContext dbContext,
        AttributeSchemaValidator attributeValidator,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var validator = new CreateProductRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "catalog.product.invalid_request",
                "Invalid product request",
                validation.Errors.First().ErrorMessage);
        }

        var brand = await dbContext.Brands.SingleOrDefaultAsync(b => b.Id == request.BrandId, cancellationToken);
        if (brand is null)
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "catalog.brand.unknown",
                "Unknown brand",
                "The requested brand does not exist.");
        }

        if (request.ManufacturerId is Guid manuId && !await dbContext.Manufacturers.AnyAsync(m => m.Id == manuId, cancellationToken))
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "catalog.manufacturer.unknown",
                "Unknown manufacturer",
                "The requested manufacturer does not exist.");
        }

        var duplicateSku = await dbContext.Products.AnyAsync(p => p.Sku == request.Sku.Trim(), cancellationToken);
        if (duplicateSku)
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status409Conflict,
                "catalog.product.sku_conflict",
                "SKU already exists",
                "A product with that SKU already exists.");
        }

        var attributesJson = request.AttributesJson ?? "{}";
        var primaryCategoryId = request.PrimaryCategoryId;
        if (primaryCategoryId is Guid catId)
        {
            var schemaRow = await dbContext.CategoryAttributeSchemas.SingleOrDefaultAsync(s => s.CategoryId == catId, cancellationToken);
            if (schemaRow is not null)
            {
                using var attrsDoc = JsonDocument.Parse(attributesJson);
                var result = await attributeValidator.ValidateAsync(catId, schemaRow.Version, schemaRow.SchemaJson, attrsDoc.RootElement, cancellationToken);
                if (!result.IsValid)
                {
                    return AdminCatalogResponseFactory.Problem(
                        context,
                        StatusCodes.Status400BadRequest,
                        "catalog.attributes.schema_violation",
                        "Attributes violate schema",
                        $"Attribute validation failed at paths: {string.Join(",", result.Errors.Select(e => e.Path))}");
                }
            }
        }

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = request.Sku.Trim(),
            Barcode = request.Barcode,
            BrandId = request.BrandId,
            ManufacturerId = request.ManufacturerId,
            SlugAr = AdminCatalogResponseFactory.NormalizeSlug(request.SlugAr),
            SlugEn = AdminCatalogResponseFactory.NormalizeSlug(request.SlugEn),
            NameAr = request.NameAr.Trim(),
            NameEn = request.NameEn.Trim(),
            ShortDescriptionAr = request.ShortDescriptionAr,
            ShortDescriptionEn = request.ShortDescriptionEn,
            DescriptionAr = request.DescriptionAr,
            DescriptionEn = request.DescriptionEn,
            AttributesJson = attributesJson,
            MarketCodes = (request.MarketCodes ?? Array.Empty<string>()).Select(m => m.Trim().ToLowerInvariant()).ToArray(),
            Status = "draft",
            Restricted = request.Restricted ?? false,
            RestrictionReasonCode = request.RestrictionReasonCode,
            RestrictionMarkets = (request.RestrictionMarkets ?? Array.Empty<string>()).Select(m => m.Trim().ToLowerInvariant()).ToArray(),
            PriceHintMinorUnits = request.PriceHintMinorUnits,
            CreatedByAccountId = AdminCatalogResponseFactory.ResolveActorAccountId(context),
        };

        dbContext.Products.Add(product);

        if (request.CategoryIds is { Length: > 0 })
        {
            for (var i = 0; i < request.CategoryIds.Length; i++)
            {
                dbContext.ProductCategories.Add(new ProductCategory
                {
                    ProductId = product.Id,
                    CategoryId = request.CategoryIds[i],
                    IsPrimary = request.CategoryIds[i] == primaryCategoryId,
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: AdminCatalogResponseFactory.ResolveActorAccountId(context),
                ActorRole: "admin",
                Action: "catalog.product.created",
                EntityType: nameof(Product),
                EntityId: product.Id,
                BeforeState: null,
                AfterState: new { product.Sku, product.BrandId, product.Status },
                Reason: "catalog.product.create"),
            cancellationToken);

        return Results.Created($"/v1/admin/catalog/products/{product.Id:N}", new { productId = product.Id });
    }

    private static async Task<IResult> UpdateAsync(
        HttpContext context,
        Guid id,
        UpdateProductRequest request,
        CatalogDbContext dbContext,
        AttributeSchemaValidator attributeValidator,
        IAuditEventPublisher auditEventPublisher,
        CatalogOutboxWriter outboxWriter,
        RestrictionCache restrictionCache,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status404NotFound,
                "catalog.product.not_found",
                "Product not found",
                "The product could not be found.");
        }

        if (product.Status != "draft" && product.Status != "in_review")
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status409Conflict,
                "catalog.product.invalid_transition",
                "Product cannot be edited in its current state",
                "Products in the published/scheduled/archived state must be mutated via dedicated transition endpoints.");
        }

        if (product.PublishedAt is not null)
        {
            if (request.SlugAr is not null && request.SlugAr != product.SlugAr)
            {
                return AdminCatalogResponseFactory.Problem(
                    context,
                    StatusCodes.Status409Conflict,
                    "catalog.slug.immutable",
                    "Slug is immutable",
                    "Product slugs cannot be changed after first publish.");
            }
            if (request.SlugEn is not null && request.SlugEn != product.SlugEn)
            {
                return AdminCatalogResponseFactory.Problem(
                    context,
                    StatusCodes.Status409Conflict,
                    "catalog.slug.immutable",
                    "Slug is immutable",
                    "Product slugs cannot be changed after first publish.");
            }
        }

        var restrictionChanged = false;
        if (request.NameAr is not null) product.NameAr = request.NameAr.Trim();
        if (request.NameEn is not null) product.NameEn = request.NameEn.Trim();
        if (request.ShortDescriptionAr is not null) product.ShortDescriptionAr = request.ShortDescriptionAr;
        if (request.ShortDescriptionEn is not null) product.ShortDescriptionEn = request.ShortDescriptionEn;
        if (request.DescriptionAr is not null) product.DescriptionAr = request.DescriptionAr;
        if (request.DescriptionEn is not null) product.DescriptionEn = request.DescriptionEn;
        if (request.SlugAr is not null) product.SlugAr = AdminCatalogResponseFactory.NormalizeSlug(request.SlugAr);
        if (request.SlugEn is not null) product.SlugEn = AdminCatalogResponseFactory.NormalizeSlug(request.SlugEn);
        if (request.PriceHintMinorUnits is long price) product.PriceHintMinorUnits = price;
        if (request.Barcode is not null) product.Barcode = request.Barcode;
        if (request.AttributesJson is not null)
        {
            // Validate against primary-category schema when one is set.
            var primaryCategoryId = await dbContext.ProductCategories
                .Where(pc => pc.ProductId == product.Id && pc.IsPrimary)
                .Select(pc => (Guid?)pc.CategoryId)
                .SingleOrDefaultAsync(cancellationToken);
            if (primaryCategoryId is Guid catIdForUpdate)
            {
                var schemaRow = await dbContext.CategoryAttributeSchemas.SingleOrDefaultAsync(s => s.CategoryId == catIdForUpdate, cancellationToken);
                if (schemaRow is not null)
                {
                    using var attrsDoc = JsonDocument.Parse(request.AttributesJson);
                    var validationResult = await attributeValidator.ValidateAsync(catIdForUpdate, schemaRow.Version, schemaRow.SchemaJson, attrsDoc.RootElement, cancellationToken);
                    if (!validationResult.IsValid)
                    {
                        return AdminCatalogResponseFactory.Problem(
                            context,
                            StatusCodes.Status400BadRequest,
                            "catalog.attributes.schema_violation",
                            "Attributes violate schema",
                            $"Attribute validation failed at paths: {string.Join(",", validationResult.Errors.Select(e => e.Path))}");
                    }
                }
            }
            product.AttributesJson = request.AttributesJson;
        }
        if (request.MarketCodes is not null) product.MarketCodes = request.MarketCodes.Select(m => m.Trim().ToLowerInvariant()).ToArray();
        if (request.Restricted is bool restricted && restricted != product.Restricted)
        {
            product.Restricted = restricted;
            restrictionChanged = true;
        }
        if (request.RestrictionReasonCode is not null)
        {
            product.RestrictionReasonCode = request.RestrictionReasonCode;
            restrictionChanged = true;
        }
        if (request.RestrictionMarkets is not null)
        {
            product.RestrictionMarkets = request.RestrictionMarkets.Select(m => m.Trim().ToLowerInvariant()).ToArray();
            restrictionChanged = true;
        }
        product.UpdatedAt = DateTimeOffset.UtcNow;

        outboxWriter.Enqueue("catalog.product.field_updated", product.Id, new { product.Id, product.Sku, product.Status });
        if (restrictionChanged)
        {
            outboxWriter.Enqueue("catalog.product.restriction_changed", product.Id, new { product.Id, product.Restricted, product.RestrictionReasonCode, product.RestrictionMarkets });
            restrictionCache.InvalidateProduct(product.Id);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: AdminCatalogResponseFactory.ResolveActorAccountId(context),
                ActorRole: "admin",
                Action: "catalog.product.updated",
                EntityType: nameof(Product),
                EntityId: product.Id,
                BeforeState: null,
                AfterState: new { product.NameAr, product.NameEn, product.Status, product.Restricted },
                Reason: "catalog.product.update"),
            cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> SubmitAsync(
        HttpContext context,
        Guid id,
        CatalogDbContext dbContext,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var machine = new ProductStateMachine();
        return await TransitionAsync(context, id, dbContext, auditEventPublisher, machine, ProductTrigger.Submit, null, cancellationToken);
    }

    private static async Task<IResult> PublishAsync(
        HttpContext context,
        Guid id,
        PublishRequest request,
        CatalogDbContext dbContext,
        IAuditEventPublisher auditEventPublisher,
        CatalogOutboxWriter outboxWriter,
        RestrictionCache restrictionCache,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status404NotFound,
                "catalog.product.not_found",
                "Product not found",
                "The product could not be found.");
        }

        if (!ProductStateMachine.TryParse(product.Status, out var from))
        {
            return InvalidTransition(context);
        }

        var publishAt = request.PublishAt;
        var isFuture = publishAt is DateTimeOffset at && at > DateTimeOffset.UtcNow;
        if (publishAt is DateTimeOffset pastAt && pastAt <= DateTimeOffset.UtcNow && publishAt is not null)
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "catalog.schedule.past_time",
                "Publish time in past",
                "The scheduled publish time must be in the future.");
        }

        var machine = new ProductStateMachine();
        var trigger = from == ProductState.Scheduled
            ? ProductTrigger.WorkerFire
            : isFuture ? ProductTrigger.PublishWithFutureAt : ProductTrigger.Publish;

        if (!machine.TryTransition(from, trigger, out var next))
        {
            return InvalidTransition(context);
        }

        if (next == ProductState.Published)
        {
            if (product.MarketCodes is null || product.MarketCodes.Length == 0)
            {
                return AdminCatalogResponseFactory.Problem(
                    context,
                    StatusCodes.Status400BadRequest,
                    "catalog.publish.market_unconfigured",
                    "No market configured",
                    "The product must have at least one market configured before publish.");
            }
            if (string.IsNullOrWhiteSpace(product.NameAr) || string.IsNullOrWhiteSpace(product.NameEn))
            {
                return AdminCatalogResponseFactory.Problem(
                    context,
                    StatusCodes.Status400BadRequest,
                    "catalog.publish.locale_required",
                    "Both locales required",
                    "Both Arabic and English names are required before publish.");
            }
            var hasMedia = await dbContext.ProductMedia.AnyAsync(m => m.ProductId == product.Id && m.IsPrimary, cancellationToken);
            if (!hasMedia)
            {
                return AdminCatalogResponseFactory.Problem(
                    context,
                    StatusCodes.Status400BadRequest,
                    "catalog.publish.media_required",
                    "Primary image required",
                    "A primary image is required before publish.");
            }
        }

        var previousStatus = product.Status;
        product.Status = ProductStateMachine.Encode(next);
        if (next == ProductState.Published)
        {
            product.PublishedAt ??= DateTimeOffset.UtcNow;
        }

        if (next == ProductState.Scheduled)
        {
            dbContext.ScheduledPublishes.Add(new ScheduledPublish
            {
                ProductId = product.Id,
                PublishAt = publishAt!.Value,
                ScheduledByAccountId = AdminCatalogResponseFactory.ResolveActorAccountId(context),
                ScheduledAt = DateTimeOffset.UtcNow,
            });
        }

        dbContext.ProductStateTransitions.Add(new ProductStateTransition
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            FromStatus = previousStatus,
            ToStatus = product.Status,
            ActorAccountId = AdminCatalogResponseFactory.ResolveActorAccountId(context),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        if (next == ProductState.Published)
        {
            outboxWriter.Enqueue("catalog.product.published", product.Id, new { product.Id, product.Sku, product.MarketCodes, product.Restricted });
            restrictionCache.InvalidateProduct(product.Id);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: AdminCatalogResponseFactory.ResolveActorAccountId(context),
                ActorRole: "admin",
                Action: $"catalog.product.{product.Status}",
                EntityType: nameof(Product),
                EntityId: product.Id,
                BeforeState: new { Status = previousStatus },
                AfterState: new { product.Status, product.PublishedAt },
                Reason: "catalog.product.transition"),
            cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> CancelScheduleAsync(
        HttpContext context,
        Guid id,
        CatalogDbContext dbContext,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null) return AdminCatalogResponseFactory.Problem(context, 404, "catalog.product.not_found", "Product not found", "The product could not be found.");
        if (!ProductStateMachine.TryParse(product.Status, out var from) || from != ProductState.Scheduled) return InvalidTransition(context);

        var machine = new ProductStateMachine();
        if (!machine.TryTransition(from, ProductTrigger.CancelSchedule, out var next)) return InvalidTransition(context);

        var scheduled = await dbContext.ScheduledPublishes.SingleOrDefaultAsync(s => s.ProductId == product.Id, cancellationToken);
        if (scheduled is not null) dbContext.ScheduledPublishes.Remove(scheduled);

        var previous = product.Status;
        product.Status = ProductStateMachine.Encode(next);
        dbContext.ProductStateTransitions.Add(new ProductStateTransition
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            FromStatus = previous,
            ToStatus = product.Status,
            ActorAccountId = AdminCatalogResponseFactory.ResolveActorAccountId(context),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: AdminCatalogResponseFactory.ResolveActorAccountId(context),
                ActorRole: "admin",
                Action: "catalog.product.schedule_cancelled",
                EntityType: nameof(Product),
                EntityId: product.Id,
                BeforeState: new { Status = previous },
                AfterState: new { product.Status },
                Reason: "catalog.product.cancel_schedule"),
            cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> ArchiveAsync(
        HttpContext context,
        Guid id,
        CatalogDbContext dbContext,
        IAuditEventPublisher auditEventPublisher,
        CatalogOutboxWriter outboxWriter,
        RestrictionCache restrictionCache,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null) return AdminCatalogResponseFactory.Problem(context, 404, "catalog.product.not_found", "Product not found", "The product could not be found.");
        if (!ProductStateMachine.TryParse(product.Status, out var from)) return InvalidTransition(context);
        var machine = new ProductStateMachine();
        if (!machine.TryTransition(from, ProductTrigger.Archive, out var next)) return InvalidTransition(context);

        var previous = product.Status;
        product.Status = ProductStateMachine.Encode(next);
        product.ArchivedAt = DateTimeOffset.UtcNow;
        dbContext.ProductStateTransitions.Add(new ProductStateTransition
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            FromStatus = previous,
            ToStatus = product.Status,
            ActorAccountId = AdminCatalogResponseFactory.ResolveActorAccountId(context),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        outboxWriter.Enqueue("catalog.product.archived", product.Id, new { product.Id, product.Sku });
        restrictionCache.InvalidateProduct(product.Id);

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: AdminCatalogResponseFactory.ResolveActorAccountId(context),
                ActorRole: "admin",
                Action: "catalog.product.archived",
                EntityType: nameof(Product),
                EntityId: product.Id,
                BeforeState: new { Status = previous },
                AfterState: new { product.Status, product.ArchivedAt },
                Reason: "catalog.product.archive"),
            cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> TransitionAsync(
        HttpContext context,
        Guid id,
        CatalogDbContext dbContext,
        IAuditEventPublisher auditEventPublisher,
        ProductStateMachine machine,
        ProductTrigger trigger,
        Func<Product, CatalogDbContext, Task>? sideEffect,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null) return AdminCatalogResponseFactory.Problem(context, 404, "catalog.product.not_found", "Product not found", "The product could not be found.");
        if (!ProductStateMachine.TryParse(product.Status, out var from)) return InvalidTransition(context);
        if (!machine.TryTransition(from, trigger, out var next)) return InvalidTransition(context);

        var previous = product.Status;
        product.Status = ProductStateMachine.Encode(next);
        dbContext.ProductStateTransitions.Add(new ProductStateTransition
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            FromStatus = previous,
            ToStatus = product.Status,
            ActorAccountId = AdminCatalogResponseFactory.ResolveActorAccountId(context),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        if (sideEffect is not null)
        {
            await sideEffect(product, dbContext);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: AdminCatalogResponseFactory.ResolveActorAccountId(context),
                ActorRole: "admin",
                Action: $"catalog.product.{trigger.ToString().ToLowerInvariant()}",
                EntityType: nameof(Product),
                EntityId: product.Id,
                BeforeState: new { Status = previous },
                AfterState: new { product.Status },
                Reason: $"catalog.product.{trigger}"),
            cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        string? status,
        string? q,
        int page,
        int pageSize,
        Guid? vendorId,
        CatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 50 : pageSize;

        var query = dbContext.Products.AsNoTracking().AsQueryable();

        // P6 multi-vendor-ready: admin list defaults to vendor_id IS NULL at launch.
        query = vendorId is Guid v
            ? query.Where(p => p.VendorId == v)
            : query.Where(p => p.VendorId == null);

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(p => p.Status == status);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.Trim()}%";
            query = query.Where(p => EF.Functions.ILike(p.Sku, like) || EF.Functions.ILike(p.NameEn, like) || EF.Functions.ILike(p.NameAr, like));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(p => p.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new AdminProductListItem(p.Id, p.Sku, p.NameAr, p.NameEn, p.Status, p.BrandId, p.MarketCodes, p.Restricted, p.PublishedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(new AdminProductListResponse(page, pageSize, total, items));
    }

    private static async Task<IResult> GetOneAsync(
        HttpContext context,
        Guid id,
        CatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null) return AdminCatalogResponseFactory.Problem(context, 404, "catalog.product.not_found", "Product not found", "The product could not be found.");

        var categories = await dbContext.ProductCategories
            .AsNoTracking()
            .Where(pc => pc.ProductId == id)
            .Select(pc => new AdminProductCategoryDto(pc.CategoryId, pc.IsPrimary))
            .ToListAsync(cancellationToken);

        var media = await dbContext.ProductMedia
            .AsNoTracking()
            .Where(m => m.ProductId == id)
            .Select(m => new AdminProductMediaDto(m.Id, m.StorageKey, m.IsPrimary, m.VariantStatus, m.AltAr, m.AltEn))
            .ToListAsync(cancellationToken);

        var dto = new AdminProductDetailResponse(
            product.Id,
            product.Sku,
            product.Barcode,
            product.BrandId,
            product.ManufacturerId,
            product.SlugAr,
            product.SlugEn,
            product.NameAr,
            product.NameEn,
            product.ShortDescriptionAr,
            product.ShortDescriptionEn,
            product.DescriptionAr,
            product.DescriptionEn,
            product.AttributesJson,
            product.MarketCodes,
            product.Status,
            product.Restricted,
            product.RestrictionReasonCode,
            product.RestrictionMarkets,
            product.PriceHintMinorUnits,
            product.PublishedAt,
            product.ArchivedAt,
            categories,
            media);
        return Results.Ok(dto);
    }

    private static IResult InvalidTransition(HttpContext context) =>
        AdminCatalogResponseFactory.Problem(
            context,
            StatusCodes.Status409Conflict,
            "catalog.product.invalid_transition",
            "Invalid product transition",
            "The requested product state transition is not allowed.");
}

public sealed record CreateProductRequest(
    string Sku,
    string? Barcode,
    Guid BrandId,
    Guid? ManufacturerId,
    string SlugAr,
    string SlugEn,
    string NameAr,
    string NameEn,
    string? ShortDescriptionAr,
    string? ShortDescriptionEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string? AttributesJson,
    string[]? MarketCodes,
    bool? Restricted,
    string? RestrictionReasonCode,
    string[]? RestrictionMarkets,
    long? PriceHintMinorUnits,
    Guid[]? CategoryIds,
    Guid? PrimaryCategoryId);

public sealed record UpdateProductRequest(
    string? Barcode,
    string? SlugAr,
    string? SlugEn,
    string? NameAr,
    string? NameEn,
    string? ShortDescriptionAr,
    string? ShortDescriptionEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string? AttributesJson,
    string[]? MarketCodes,
    bool? Restricted,
    string? RestrictionReasonCode,
    string[]? RestrictionMarkets,
    long? PriceHintMinorUnits);

public sealed record PublishRequest(DateTimeOffset? PublishAt);

public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SlugAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SlugEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(400);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(400);
        RuleFor(x => x.BrandId).NotEmpty();
    }
}

public sealed record AdminProductListItem(Guid Id, string Sku, string NameAr, string NameEn, string Status, Guid BrandId, string[] MarketCodes, bool Restricted, DateTimeOffset? PublishedAt);
public sealed record AdminProductListResponse(int Page, int PageSize, int Total, IReadOnlyList<AdminProductListItem> Items);

public sealed record AdminProductCategoryDto(Guid CategoryId, bool IsPrimary);
public sealed record AdminProductMediaDto(Guid MediaId, string StorageKey, bool IsPrimary, string VariantStatus, string? AltAr, string? AltEn);
public sealed record AdminProductDetailResponse(
    Guid Id,
    string Sku,
    string? Barcode,
    Guid BrandId,
    Guid? ManufacturerId,
    string SlugAr,
    string SlugEn,
    string NameAr,
    string NameEn,
    string? ShortDescriptionAr,
    string? ShortDescriptionEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string AttributesJson,
    string[] MarketCodes,
    string Status,
    bool Restricted,
    string? RestrictionReasonCode,
    string[] RestrictionMarkets,
    long? PriceHintMinorUnits,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ArchivedAt,
    IReadOnlyList<AdminProductCategoryDto> Categories,
    IReadOnlyList<AdminProductMediaDto> Media);
