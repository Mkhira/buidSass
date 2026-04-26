using System.Security.Cryptography;
using BackendApi.Modules.Returns.Common;
using BackendApi.Modules.Returns.Entities;
using BackendApi.Modules.Returns.Persistence;
using BackendApi.Modules.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Returns.Customer.UploadReturnPhoto;

public static class Endpoint
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedMimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/heic", "image/heif",
    };

    public static IEndpointRouteBuilder MapUploadReturnPhotoEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/photos", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" })
            .DisableAntiforgery();
        return builder;
    }

    /// <summary>
    /// FR-020. Multipart photo upload for return evidence. Caller obtains a <c>photoId</c> and
    /// later passes it to <c>POST /v1/customer/orders/{orderId}/returns</c>. Photos are
    /// orphan-tolerant: an unused photo simply ages out (Phase 1.5 cleanup worker).
    /// </summary>
    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IFormFile file,
        ReturnsDbContext db,
        IStorageService storage,
        CancellationToken ct)
    {
        var accountId = ReturnsResponseFactory.ResolveAccountId(context);
        if (accountId is null)
        {
            return ReturnsResponseFactory.Problem(context, 401, "returns.requires_auth", "Auth required");
        }
        if (file is null || file.Length == 0)
        {
            return ReturnsResponseFactory.Problem(context, 400, "photo.empty", "File is empty.");
        }
        if (file.Length > MaxBytes)
        {
            return ReturnsResponseFactory.Problem(context, 413, "photo.size.exceeded",
                $"File exceeds {MaxBytes} bytes.");
        }
        if (!AllowedMimes.Contains(file.ContentType ?? string.Empty))
        {
            return ReturnsResponseFactory.Problem(context, 415, "photo.mime.unsupported",
                $"Unsupported content type '{file.ContentType}'.");
        }

        // Pull the bytes once; we need both a SHA hash for tamper detection AND a copy to
        // upload. Buffering 5 MB is acceptable in this hot path.
        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        ms.Position = 0;
        var sha = Convert.ToHexString(SHA256.HashData(ms.ToArray())).ToLowerInvariant();
        ms.Position = 0;

        // Resolve market from the JWT — falls back to KSA when absent. The market only affects
        // the storage residency partition (ADR-010), not validation.
        var marketClaim = context.User.FindFirst("market")?.Value;
        var market = string.Equals(marketClaim, "EG", StringComparison.OrdinalIgnoreCase)
            ? MarketCode.EG
            : MarketCode.KSA;

        StoredFileResult stored;
        try
        {
            stored = await storage.UploadAsync(ms, file.FileName ?? "return_photo", file.ContentType!, market, ct);
        }
        catch (StorageUploadBlockedException ex)
        {
            return ReturnsResponseFactory.Problem(context, 422, "photo.upload.blocked", ex.Message);
        }

        var photo = new ReturnPhoto
        {
            Id = stored.FileId,
            ReturnRequestId = null,
            AccountId = accountId.Value,
            BlobKey = stored.FileId.ToString("N"),
            Mime = file.ContentType!,
            SizeBytes = file.Length,
            Sha256 = sha,
            UploadedAt = DateTimeOffset.UtcNow,
        };
        db.ReturnPhotos.Add(photo);
        await db.SaveChangesAsync(ct);

        return Results.Json(new { photoId = photo.Id }, statusCode: 201);
    }
}
