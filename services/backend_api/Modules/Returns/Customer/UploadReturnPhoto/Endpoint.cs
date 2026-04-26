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
        var bytes = ms.ToArray();
        // CR Major: do NOT trust file.ContentType — the multipart header is client-controlled.
        // Validate the leading magic bytes against the AllowedMimes set so a renamed
        // executable cannot be stored as an image.
        if (!IsAllowedImageSignature(bytes, file.ContentType ?? string.Empty))
        {
            return ReturnsResponseFactory.Problem(context, 415, "photo.mime.unsupported",
                "Uploaded bytes do not match a supported image format (JPEG/PNG/HEIC).");
        }
        ms.Position = 0;
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
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
            MarketCode = market.ToString(),
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

    /// <summary>
    /// Verifies the leading bytes match the claimed MIME type. Defends against renamed
    /// payloads (a .exe relabelled `image/jpeg` would otherwise pass MIME-only validation).
    /// </summary>
    private static bool IsAllowedImageSignature(byte[] bytes, string mime)
    {
        if (bytes.Length < 4) return false;
        // JPEG: FF D8 FF
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return mime.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase);
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            return mime.Equals("image/png", StringComparison.OrdinalIgnoreCase);
        // HEIC/HEIF: bytes 4..11 == "ftypheic" / "ftypheix" / "ftypmif1" / "ftyphevc" / "ftypheim" / "ftypheis" / "ftyphevm" / "ftyphevs" / "ftypmsf1"
        if (bytes.Length >= 12 && bytes[4] == (byte)'f' && bytes[5] == (byte)'t' && bytes[6] == (byte)'y' && bytes[7] == (byte)'p')
        {
            var brand = System.Text.Encoding.ASCII.GetString(bytes, 8, 4);
            string[] heifBrands = ["heic", "heix", "heim", "heis", "hevc", "hevm", "hevs", "mif1", "msf1"];
            if (heifBrands.Contains(brand))
                return mime.Equals("image/heic", StringComparison.OrdinalIgnoreCase)
                    || mime.Equals("image/heif", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}
