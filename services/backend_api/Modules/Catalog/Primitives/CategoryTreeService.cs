using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Catalog.Primitives;

public sealed class CategoryTreeService
{
    /// <summary>
    /// Inserts closure rows for a newly created category. Copies ancestor rows of the parent
    /// and appends a self-reference (depth 0).
    /// </summary>
    public async Task InsertAsync(CatalogDbContext dbContext, Guid categoryId, Guid? parentId, CancellationToken cancellationToken)
    {
        dbContext.CategoryClosures.Add(new CategoryClosure
        {
            AncestorId = categoryId,
            DescendantId = categoryId,
            Depth = 0,
        });

        if (parentId is null)
        {
            return;
        }

        var parentAncestors = await dbContext.CategoryClosures
            .Where(c => c.DescendantId == parentId)
            .ToListAsync(cancellationToken);

        foreach (var ancestor in parentAncestors)
        {
            dbContext.CategoryClosures.Add(new CategoryClosure
            {
                AncestorId = ancestor.AncestorId,
                DescendantId = categoryId,
                Depth = ancestor.Depth + 1,
            });
        }
    }

    /// <summary>
    /// Moves a subtree to a new parent. Rejects cycles. Rewrites ancestor rows for every descendant.
    /// </summary>
    public async Task<ReparentResult> ReparentAsync(CatalogDbContext dbContext, Guid categoryId, Guid? newParentId, CancellationToken cancellationToken)
    {
        if (newParentId is Guid target)
        {
            var isCycle = await dbContext.CategoryClosures
                .AnyAsync(c => c.AncestorId == categoryId && c.DescendantId == target, cancellationToken);
            if (isCycle)
            {
                return ReparentResult.Cycle;
            }
        }

        var subtreeDescendantIds = await dbContext.CategoryClosures
            .Where(c => c.AncestorId == categoryId)
            .Select(c => c.DescendantId)
            .ToListAsync(cancellationToken);

        var subtreeSelfClosure = await dbContext.CategoryClosures
            .Where(c => c.AncestorId == categoryId && subtreeDescendantIds.Contains(c.DescendantId))
            .ToListAsync(cancellationToken);

        var staleAncestorRows = await dbContext.CategoryClosures
            .Where(c => subtreeDescendantIds.Contains(c.DescendantId) && !subtreeDescendantIds.Contains(c.AncestorId))
            .ToListAsync(cancellationToken);

        dbContext.CategoryClosures.RemoveRange(staleAncestorRows);

        if (newParentId is Guid newParent)
        {
            var newParentAncestors = await dbContext.CategoryClosures
                .Where(c => c.DescendantId == newParent)
                .ToListAsync(cancellationToken);

            foreach (var ancestor in newParentAncestors)
            {
                foreach (var self in subtreeSelfClosure)
                {
                    dbContext.CategoryClosures.Add(new CategoryClosure
                    {
                        AncestorId = ancestor.AncestorId,
                        DescendantId = self.DescendantId,
                        Depth = ancestor.Depth + self.Depth + 1,
                    });
                }
            }
        }

        var category = await dbContext.Categories.SingleAsync(c => c.Id == categoryId, cancellationToken);
        category.ParentId = newParentId;
        category.UpdatedAt = DateTimeOffset.UtcNow;

        return ReparentResult.Ok;
    }

    /// <summary>
    /// Detaches a category: removes closure rows where it participates as descendant-under-others,
    /// plus the self-reference. Callers must ensure no products reference the category.
    /// </summary>
    public async Task DetachAsync(CatalogDbContext dbContext, Guid categoryId, CancellationToken cancellationToken)
    {
        var descendantIds = await dbContext.CategoryClosures
            .Where(c => c.AncestorId == categoryId)
            .Select(c => c.DescendantId)
            .ToListAsync(cancellationToken);

        var rows = await dbContext.CategoryClosures
            .Where(c => descendantIds.Contains(c.DescendantId) || descendantIds.Contains(c.AncestorId))
            .ToListAsync(cancellationToken);
        dbContext.CategoryClosures.RemoveRange(rows);
    }
}

public enum ReparentResult
{
    Ok = 0,
    Cycle = 1,
}
