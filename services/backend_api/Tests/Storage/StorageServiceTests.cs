using BackendApi.Modules.Shared;
using BackendApi.Modules.Storage;
using Microsoft.EntityFrameworkCore;

namespace backend_api.Tests.Storage;

public sealed class StorageServiceTests
{
    [Fact]
    public async Task Upload_Writes_File_Record_And_Returns_SignedUrl()
    {
        await using var db = CreateDb();
        var service = new LocalDiskStorageService(db, new LocalVirusScanService());
        await using var stream = new MemoryStream([1, 2, 3, 4]);

        var result = await service.UploadAsync(stream, "proof.txt", "text/plain", MarketCode.KSA, CancellationToken.None);

        var stored = await db.StoredFiles.SingleAsync();
        var expectedPath = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "storage", "KSA", Path.GetFileName(stored.BucketKey));
        Assert.True(File.Exists(expectedPath));
        Assert.Equal("KSA", stored.Market);
        Assert.StartsWith("http://localhost:5000/dev-storage/", result.SignedUrl.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upload_When_Scanner_Unavailable_Throws_And_Persists_Nothing()
    {
        await using var db = CreateDb();
        var service = new LocalDiskStorageService(db, new UnavailableScanner());
        await using var stream = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<StorageUploadBlockedException>(() =>
            service.UploadAsync(stream, "blocked.bin", "application/octet-stream", MarketCode.KSA, CancellationToken.None));

        Assert.False(await db.StoredFiles.AnyAsync());
    }

    [Fact]
    public async Task Upload_With_Ksa_Market_Routes_To_Ksa_Directory()
    {
        await using var db = CreateDb();
        var service = new LocalDiskStorageService(db, new LocalVirusScanService());
        await using var stream = new MemoryStream([8, 9]);

        var result = await service.UploadAsync(stream, "ksa.txt", "text/plain", MarketCode.KSA, CancellationToken.None);
        var stored = await db.StoredFiles.SingleAsync(x => x.Id == result.FileId);

        Assert.StartsWith("KSA/", stored.BucketKey, StringComparison.Ordinal);
        Assert.DoesNotContain("EG/", stored.BucketKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upload_With_Eg_Market_Routes_To_Eg_Directory()
    {
        await using var db = CreateDb();
        var service = new LocalDiskStorageService(db, new LocalVirusScanService());
        await using var stream = new MemoryStream([7, 7, 7]);

        var result = await service.UploadAsync(stream, "eg.txt", "text/plain", MarketCode.EG, CancellationToken.None);
        var stored = await db.StoredFiles.SingleAsync(x => x.Id == result.FileId);

        Assert.StartsWith("EG/", stored.BucketKey, StringComparison.Ordinal);
        Assert.DoesNotContain("KSA/", stored.BucketKey, StringComparison.Ordinal);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private sealed class UnavailableScanner : IVirusScanService
    {
        public Task<ScanResult> ScanAsync(Stream content, CancellationToken cancellationToken)
        {
            return Task.FromResult(ScanResult.ServiceUnavailable);
        }
    }
}
