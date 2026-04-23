using BackendApi.Modules.Catalog.Customer.Common;
using BackendApi.Modules.Catalog.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Catalog.Customer.ListCategories;

public static class ListCategoriesEndpoint
{
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/categories", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        string? market,
        CatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var marketCode = CustomerCatalogResponseFactory.ResolveMarket(context, market);

        var categories = await dbContext.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Slug)
            .Select(c => new CategoryNode(
                c.Id,
                c.Slug,
                c.NameAr,
                c.NameEn,
                c.ParentId,
                c.DisplayOrder))
            .ToListAsync(cancellationToken);

        var tree = BuildTree(categories);
        return Results.Ok(new ListCategoriesResponse(tree, marketCode));
    }

    private static IReadOnlyList<CategoryTreeNode> BuildTree(IReadOnlyList<CategoryNode> flat)
    {
        var byParent = flat.ToLookup(c => c.ParentId);
        return BuildChildren(null, byParent, depth: 0);
    }

    private static IReadOnlyList<CategoryTreeNode> BuildChildren(Guid? parentId, ILookup<Guid?, CategoryNode> byParent, int depth)
    {
        var siblings = byParent[parentId]
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Slug)
            .ToList();

        var nodes = new List<CategoryTreeNode>(siblings.Count);
        foreach (var sibling in siblings)
        {
            nodes.Add(new CategoryTreeNode(
                sibling.Id,
                sibling.Slug,
                sibling.NameAr,
                sibling.NameEn,
                depth,
                BuildChildren(sibling.Id, byParent, depth + 1)));
        }

        return nodes;
    }
}

internal sealed record CategoryNode(Guid Id, string Slug, string NameAr, string NameEn, Guid? ParentId, int DisplayOrder);

public sealed record CategoryTreeNode(Guid Id, string Slug, string NameAr, string NameEn, int Depth, IReadOnlyList<CategoryTreeNode> Children);

public sealed record ListCategoriesResponse(IReadOnlyList<CategoryTreeNode> Categories, string Market);
