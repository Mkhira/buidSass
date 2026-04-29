using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Verification.Admin.Common;
using BackendApi.Modules.Verification.Admin.GetVerificationDetail;
using BackendApi.Modules.Verification.Customer.GetMarketSchema;
using BackendApi.Modules.Verification.Customer.SubmitVerification;
using BackendApi.Modules.Verification.Eligibility;
using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using BackendApi.Modules.Verification.Seeding;
using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace Verification.Tests.Integration;

/// <summary>
/// Spec 020 task T098. Verifies per-market schema versioning behavior:
/// <list type="bullet">
///   <item>Inserting a v2 KSA schema (with old v1 effective_to=now) makes new submissions validate against v2.</item>
///   <item>The reviewer detail of an existing v1-snapshotted verification renders v1 fields/labels (FR-026 / SC-010).</item>
///   <item>GetMarketSchema endpoint returns the active version.</item>
/// </list>
/// </summary>
public sealed class MarketSchemaVersioningTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_schema_versioning_test")
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

    private const string SchemaV1RequiredFieldsJson = """
[
  {"name":"profession","kind":"enum","required":true,"pattern":null,"enumValues":["dentist","dental_lab_tech","dental_student","clinic_buyer"],"labelKeyEn":"verification.field.profession.label","labelKeyAr":"verification.field.profession.label"},
  {"name":"regulator_identifier","kind":"text","required":true,"pattern":"^[A-Z0-9-]{6,20}$","enumValues":null,"labelKeyEn":"verification.field.regulator_identifier.ksa.label","labelKeyAr":"verification.field.regulator_identifier.ksa.label"}
]
""";

    private const string SchemaV2RequiredFieldsJson = """
[
  {"name":"profession","kind":"enum","required":true,"pattern":null,"enumValues":["dentist","periodontist"],"labelKeyEn":"verification.field.profession.v2.label","labelKeyAr":"verification.field.profession.v2.label"},
  {"name":"regulator_identifier","kind":"text","required":true,"pattern":"^SCFHS-V2-[0-9]{8}$","enumValues":null,"labelKeyEn":"verification.field.regulator_identifier.v2.label","labelKeyAr":"verification.field.regulator_identifier.v2.label"}
]
""";

    [Fact]
    public async Task V1_submission_validates_against_v1_schema()
    {
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitV1Async(customerId, profession: "dentist", regulatorId: "SCFHS-1234567");
        verificationId.Should().NotBe(Guid.Empty);

        await using var db = NewContext();
        var row = await db.Verifications.AsNoTracking().SingleAsync(v => v.Id == verificationId);
        row.SchemaVersion.Should().Be(1);
    }

    [Fact]
    public async Task After_publishing_v2_new_submissions_validate_against_v2_pattern()
    {
        // Submit a v1-flavored row before the v2 publish — fine.
        var customerA = Guid.NewGuid();
        await SubmitV1Async(customerA, profession: "dentist", regulatorId: "SCFHS-1111111");

        // Publish v2: in one Tx mark v1.effective_to=now and INSERT v2.
        var publishAt = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
        await PublishV2Async(publishAt);

        // Now submit with a regulator id that matches v1's regex but NOT v2's
        // — must reject (the new schema is in force).
        var customerB = Guid.NewGuid();
        await using var db = NewContext();
        var submit = new SubmitVerificationHandler(
            db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
            new FakeTimeProvider(publishAt.AddDays(1)),
            NullLogger<SubmitVerificationHandler>.Instance);
        var result = await submit.HandleAsync(customerB, "ksa",
            new SubmitVerificationRequest("dentist", "SCFHS-9999999",
                Array.Empty<Guid>(), null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse(
            "the v1-format regulator id must fail v2's tighter pattern (SCFHS-V2-NNNNNNNN)");
        result.ReasonCode.Should().Be(VerificationReasonCode.RegulatorIdentifierInvalid);
    }

    [Fact]
    public async Task V2_compliant_submission_is_accepted_after_publish()
    {
        var publishAt = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
        await PublishV2Async(publishAt);

        var customerId = Guid.NewGuid();
        await using var db = NewContext();
        var submit = new SubmitVerificationHandler(
            db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
            new FakeTimeProvider(publishAt.AddDays(1)),
            NullLogger<SubmitVerificationHandler>.Instance);
        var result = await submit.HandleAsync(customerId, "ksa",
            new SubmitVerificationRequest("dentist", "SCFHS-V2-12345678",
                Array.Empty<Guid>(), null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue($"v2-format submit should pass: {result.Detail}");

        await using var verify = NewContext();
        var row = await verify.Verifications.AsNoTracking()
            .SingleAsync(v => v.Id == result.Response!.Id);
        row.SchemaVersion.Should().Be(2);
    }

    [Fact]
    public async Task GetMarketSchema_returns_active_version_after_v2_publish()
    {
        var publishAt = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
        await PublishV2Async(publishAt);

        await using var db = NewContext();
        var handler = new GetMarketSchemaHandler(db);
        var result = await handler.HandleAsync("ksa", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Version.Should().Be(2);
        result.RequiredFieldsJson.Should().Contain("SCFHS-V2");
    }

    [Fact]
    public async Task V1_verification_detail_renders_v1_schema_after_v2_publish()
    {
        // Submit v1, then publish v2 — the v1 row's schema_version is locked,
        // so its detail must render the v1 required_fields (FR-026 / SC-010).
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitV1Async(customerId, profession: "dentist", regulatorId: "SCFHS-1234567");
        await PublishV2Async(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));

        await using var db = NewContext();
        var handler = new GetVerificationDetailHandler(
            db, new BackendApi.Modules.Shared.NullRegulatorAssistLookup(), new TestPiiRecorder());
        var reviewerMarkets = new HashSet<string>(StringComparer.Ordinal) { "ksa" };
        var detail = await handler.HandleAsync(verificationId, reviewerMarkets, CancellationToken.None);

        detail.Exists.Should().BeTrue();
        detail.Response!.SchemaVersion.Should().Be(1);
        detail.Response.SchemaSnapshot.Version.Should().Be(1);
        detail.Response.SchemaSnapshot.RequiredFieldsJson.Should().Contain("[A-Z0-9-]");
        detail.Response.SchemaSnapshot.RequiredFieldsJson.Should().NotContain("SCFHS-V2");
    }

    // ────────────────────────── helpers ──────────────────────────

    private async Task<Guid> SubmitV1Async(Guid customerId, string profession, string regulatorId)
    {
        await using var db = NewContext();
        var submit = new SubmitVerificationHandler(
            db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
            new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero)),
            NullLogger<SubmitVerificationHandler>.Instance);
        var result = await submit.HandleAsync(customerId, "ksa",
            new SubmitVerificationRequest(profession, regulatorId, Array.Empty<Guid>(), null),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue($"v1 submit: {result.Detail}");
        return result.Response!.Id;
    }

    private async Task PublishV2Async(DateTimeOffset publishAt)
    {
        await using var db = NewContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        // Mark v1 retired + insert v2 in one Tx (matches the runbook procedure).
        var v1 = await db.MarketSchemas
            .SingleAsync(s => s.MarketCode == "ksa" && s.Version == 1);
        v1.EffectiveTo = publishAt;

        db.MarketSchemas.Add(new VerificationMarketSchema
        {
            MarketCode = "ksa",
            Version = 2,
            EffectiveFrom = publishAt,
            EffectiveTo = null,
            RequiredFieldsJson = SchemaV2RequiredFieldsJson,
            AllowedDocumentTypesJson = "[\"application/pdf\",\"image/jpeg\",\"image/png\",\"image/heic\"]",
            RetentionMonths = 24,
            CooldownDays = 7,
            ExpiryDays = 365,
            ReminderWindowsDaysJson = "[30,14,7,1]",
            HolidaysListJson = "[]",
            SlaDecisionBusinessDays = 2,
            SlaWarningBusinessDays = 1,
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
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

    private sealed class RecordingAuditPublisher : IAuditEventPublisher
    {
        public Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestPiiRecorder : BackendApi.Modules.Verification.Primitives.IPiiAccessRecorder
    {
        public Task RecordAsync(BackendApi.Modules.Verification.Primitives.PiiAccessKind kind, Guid verificationId, Guid? documentId, CancellationToken ct)
            => Task.CompletedTask;
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
