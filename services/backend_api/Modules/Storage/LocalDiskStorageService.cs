using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Storage;

public sealed class LocalDiskStorageService(AppDbContext dbContext, IVirusScanService virusScanService) : IStorageService
{
    public async Task<StoredFileResult> UploadAsync(
        Stream content,
        string fileName,
        string mimeType,
        MarketCode market,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        using var memory = new MemoryStream();
        await content.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;

        var scanResult = await virusScanService.ScanAsync(memory, cancellationToken);
        if (scanResult is ScanResult.Infected or ScanResult.ServiceUnavailable)
        {
            throw new StorageUploadBlockedException($"Upload blocked due to scan result: {scanResult}");
        }

        var fileId = Guid.NewGuid();
        var marketFolder = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "storage", market.ToString());
        Directory.CreateDirectory(marketFolder);

        var safeFileName = $"{fileId}-{Path.GetFileName(fileName)}";
        var relativeKey = Path.Combine(market.ToString(), safeFileName).Replace('\\', '/');
        var fullPath = Path.Combine(marketFolder, safeFileName);

        memory.Position = 0;
        await using (var file = File.Create(fullPath))
        {
            await memory.CopyToAsync(file, cancellationToken);
        }

        var stored = new StoredFile
        {
            Id = fileId,
            BucketKey = relativeKey,
            Market = market.ToString(),
            OriginalFilename = fileName,
            SizeBytes = memory.Length,
            MimeType = mimeType,
            VirusScanStatus = ScanResult.Clean.ToString().ToLowerInvariant(),
            UploadedByActorId = TryResolveActorId(),
            UploadedAt = DateTimeOffset.UtcNow,
        };

        dbContext.StoredFiles.Add(stored);
        await dbContext.SaveChangesAsync(cancellationToken);

        var signedUrl = new Uri($"http://localhost:5000/dev-storage/{fileId}");
        return new StoredFileResult(fileId, signedUrl, market);
    }

    public async Task<Uri> GetSignedUrlAsync(string fileId, TimeSpan expiry, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(fileId, out var parsed))
        {
            throw new FileNotFoundException($"Invalid file id: {fileId}");
        }

        var exists = await dbContext.StoredFiles.AnyAsync(x => x.Id == parsed, cancellationToken);
        if (!exists)
        {
            throw new FileNotFoundException($"File {fileId} not found");
        }

        var expires = DateTimeOffset.UtcNow.Add(expiry).ToUnixTimeSeconds();
        return new Uri($"http://localhost:5000/dev-storage/{fileId}?exp={expires}");
    }

    public async Task DeleteAsync(string fileId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(fileId, out var parsed))
        {
            throw new FileNotFoundException($"Invalid file id: {fileId}");
        }

        var stored = await dbContext.StoredFiles.FirstOrDefaultAsync(x => x.Id == parsed, cancellationToken);
        if (stored is null)
        {
            throw new FileNotFoundException($"File {fileId} not found");
        }

        var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "storage", stored.BucketKey.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        dbContext.StoredFiles.Remove(stored);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Guid? TryResolveActorId()
    {
        return null;
    }
}
