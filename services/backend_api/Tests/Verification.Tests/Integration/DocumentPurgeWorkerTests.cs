using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Storage;
using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Seeding;
using BackendApi.Modules.Verification.Workers;
using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace Verification.Tests.Integration;

/// <summary>
/// Spec 020 task T091. Verifies VerificationDocumentPurgeWorker:
/// <list type="bullet">
///   <item>document body deleted via IStorageService when purge_after &lt;= now,</item>
///   <item>row preserved with purged_at + storage_key=null,</item>
///   <item>verification.document_purged audit event written,</item>
///   <item>storage failure does not block the row update (best-effort).</item>
/// </list>
/// </summary>
public sealed class DocumentPurgeWorkerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_purge_worker_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    private string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = NewContext();
        await ctx.Database.MigrateAsync();
        await SeedSchemasAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private VerificationDbContext NewContext() => new(
        new DbContextOptionsBuilder<VerificationDbContext>().UseNpgsql(ConnectionString).Options);

    [Fact]
    public async Task Worker_purges_documents_past_purge_after()
    {
        var (verificationId, documentId) = await SeedDocumentAsync(
            purgeAfter: new DateTimeOffset(2027, 5, 1, 0, 0, 0, TimeSpan.Zero),
            storageKey: "test/key/doc-1");

        var snapshot = new DateTimeOffset(2027, 5, 2, 9, 0, 0, TimeSpan.Zero);
        var (worker, audit, storage) = BuildWorker(snapshot);

        var purged = await worker.RunPassAsync(CancellationToken.None);

        purged.Should().Be(1);
        storage.Deleted.Should().Contain("test/key/doc-1");

        await using var db = NewContext();
        var doc = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == documentId);
        doc.PurgedAt.Should().Be(snapshot);
        doc.StorageKey.Should().BeNull("purged rows clear storage_key so reads cannot reach the deleted body");

        audit.Events.Should().Contain(e =>
            e.Action == "verification.document_purged"
            && e.EntityId == documentId
            && e.Reason == "retention_window_elapsed");
    }

    [Fact]
    public async Task Worker_skips_documents_not_yet_due()
    {
        await SeedDocumentAsync(
            purgeAfter: new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero),
            storageKey: "test/key/future");

        var snapshot = new DateTimeOffset(2027, 5, 2, 9, 0, 0, TimeSpan.Zero);
        var (worker, _, storage) = BuildWorker(snapshot);

        var purged = await worker.RunPassAsync(CancellationToken.None);

        purged.Should().Be(0);
        storage.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task Worker_is_idempotent_on_already_purged_rows()
    {
        var (_, documentId) = await SeedDocumentAsync(
            purgeAfter: new DateTimeOffset(2027, 5, 1, 0, 0, 0, TimeSpan.Zero),
            storageKey: "test/key/doc-2");

        var snapshot = new DateTimeOffset(2027, 5, 2, 9, 0, 0, TimeSpan.Zero);
        var (worker, audit, _) = BuildWorker(snapshot);

        var first = await worker.RunPassAsync(CancellationToken.None);
        var second = await worker.RunPassAsync(CancellationToken.None);

        first.Should().Be(1);
        second.Should().Be(0, "row already purged → no second pass");
        audit.Events.Count(e => e.EntityId == documentId).Should().Be(1);
    }

    [Fact]
    public async Task Storage_delete_failure_still_marks_row_purged()
    {
        var (_, documentId) = await SeedDocumentAsync(
            purgeAfter: new DateTimeOffset(2027, 5, 1, 0, 0, 0, TimeSpan.Zero),
            storageKey: "test/key/will-fail");

        var snapshot = new DateTimeOffset(2027, 5, 2, 9, 0, 0, TimeSpan.Zero);
        var (worker, _, storage) = BuildWorker(snapshot, throwOnDelete: true);

        var purged = await worker.RunPassAsync(CancellationToken.None);
        purged.Should().Be(1);

        await using var db = NewContext();
        var doc = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == documentId);
        doc.PurgedAt.Should().Be(snapshot,
            "best-effort storage delete: row is marked purged regardless");
        doc.StorageKey.Should().BeNull();
        storage.DeleteAttempts.Should().Be(1);
    }

    // ────────────────────────── helpers ──────────────────────────

    private (VerificationDocumentPurgeWorker worker, ExpiryWorkerTests.RecordingAuditPublisher audit, FakeStorage storage) BuildWorker(
        DateTimeOffset snapshot, bool throwOnDelete = false)
    {
        var clock = new FakeTimeProvider(snapshot);
        var audit = new ExpiryWorkerTests.RecordingAuditPublisher();
        var storage = new FakeStorage(throwOnDelete);
        var services = new ServiceCollection();
        services.AddDbContext<VerificationDbContext>(o => o.UseNpgsql(ConnectionString),
            ServiceLifetime.Scoped);
        services.AddSingleton<IAuditEventPublisher>(audit);
        services.AddSingleton<IStorageService>(storage);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var options = Options.Create(new VerificationWorkerOptions());
        var worker = new VerificationDocumentPurgeWorker(scopeFactory, options, clock,
            NullLogger<VerificationDocumentPurgeWorker>.Instance);
        return (worker, audit, storage);
    }

    private async Task<(Guid verificationId, Guid documentId)> SeedDocumentAsync(
        DateTimeOffset purgeAfter,
        string storageKey)
    {
        var verificationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        await using var db = NewContext();
        db.Verifications.Add(new BackendApi.Modules.Verification.Entities.Verification
        {
            Id = verificationId,
            CustomerId = Guid.NewGuid(),
            MarketCode = "ksa",
            SchemaVersion = 1,
            Profession = "dentist",
            RegulatorIdentifier = "SCFHS-1234567",
            State = BackendApi.Modules.Verification.Primitives.VerificationState.Approved,
            SubmittedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.Documents.Add(new VerificationDocument
        {
            Id = documentId,
            VerificationId = verificationId,
            MarketCode = "ksa",
            StorageKey = storageKey,
            ContentType = "application/pdf",
            SizeBytes = 1024,
            ScanStatus = "clean",
            UploadedAt = DateTimeOffset.UtcNow,
            PurgeAfter = purgeAfter,
        });
        await db.SaveChangesAsync();
        return (verificationId, documentId);
    }

    private async Task SeedSchemasAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<VerificationDbContext>(o => o.UseNpgsql(ConnectionString));
        var provider = services.BuildServiceProvider();
        var seeder = new VerificationReferenceDataSeeder();
        var ctx = new SeedContext(
            Db: null!, Services: provider, Size: DatasetSize.Small,
            Env: new TestHostEnv(), Logger: NullLogger.Instance);
        await seeder.ApplyAsync(ctx, CancellationToken.None);
    }

    private sealed class FakeStorage : IStorageService
    {
        private readonly bool _throwOnDelete;
        public List<string> Deleted { get; } = new();
        public int DeleteAttempts { get; private set; }

        public FakeStorage(bool throwOnDelete) => _throwOnDelete = throwOnDelete;

        public Task<StoredFileResult> UploadAsync(Stream content, string fileName, string mimeType, MarketCode market, CancellationToken cancellationToken)
            => throw new NotImplementedException();
        public Task<Uri> GetSignedUrlAsync(string fileId, TimeSpan expiry, CancellationToken cancellationToken)
            => Task.FromResult(new Uri($"https://test/{fileId}"));
        public Task DeleteAsync(string fileId, CancellationToken cancellationToken)
        {
            DeleteAttempts++;
            if (_throwOnDelete) throw new IOException("simulated storage failure");
            Deleted.Add(fileId);
            return Task.CompletedTask;
        }
    }

    private sealed class TestHostEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Verification.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
