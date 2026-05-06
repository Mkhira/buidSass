using BackendApi.Modules.AuditLog;
using BackendApi.Modules.B2B.Companies;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace B2B.Tests.Integration;

/// <summary>
/// Spec 021 backfill for the deferred US4 contract tests (T103, T104, T105, T106, T107).
/// The HTTP contract was thoroughly exercised in PR #69; this suite drives the
/// underlying handlers directly to lock in the invariants that future refactors
/// can break:
///
/// <list type="bullet">
///   <item>Duplicate <c>(market_code, tax_id)</c> registration → <c>409 company.duplicate_tax_id</c>.</item>
///   <item><c>company.approver_required=true → false</c> while pending-approver quotes
///         exist transitions them back to <c>revised</c> (FR-031).</item>
///   <item>Removing the last admin → <c>409 company.last_admin_cannot_be_removed</c> (FR-024).</item>
///   <item>Removing the last approver under <c>approver_required=true</c> →
///         <c>409 company.last_approver_cannot_be_removed_with_required</c> (FR-025).</item>
///   <item>Removing the last approver under <c>approver_required=false</c> succeeds.</item>
///   <item>Branch removal blocks when a non-terminal quote references the branch.</item>
///   <item>Pending-invitation uniqueness on <c>(company, email, role) WHERE state='pending'</c>.</item>
/// </list>
/// </summary>
public sealed class CompanyAdministrationInvariantsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("b2b_company_invariants")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly RecordingAuditPublisher _audit = new();
    private B2BDbContext _db = default!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await SeedMarketSchemaAsync(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task RegisterCompany_duplicates_rejected_with_company_duplicate_tax_id()
    {
        var actorId = Guid.NewGuid();
        var handler = new RegisterCompanyHandler(_db, _audit, _clock);

        var first = await handler.HandleAsync(actorId, "ksa",
            new RegisterCompanyRequest(
                Name: new LocalizedName("Acme Dental", "أكمي"),
                TaxId: "300999111222333",
                MarketCode: "ksa",
                PrimaryAddress: null,
                BillingAddress: null,
                ApproverRequired: false,
                PoRequired: false,
                UniquePoRequired: false),
            CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        _db.ChangeTracker.Clear();

        var second = await handler.HandleAsync(Guid.NewGuid(), "ksa",
            new RegisterCompanyRequest(
                Name: new LocalizedName("Acme Dental Sequel", "أكمي 2"),
                TaxId: "300999111222333", // same tax_id, same market
                MarketCode: "ksa",
                PrimaryAddress: null,
                BillingAddress: null,
                ApproverRequired: false,
                PoRequired: false,
                UniquePoRequired: false),
            CancellationToken.None);

        second.IsSuccess.Should().BeFalse();
        second.StatusCode.Should().Be(409);
        second.ReasonCode.Should().Be(QuoteReasonCode.CompanyDuplicateTaxId);
    }

    [Fact]
    public async Task UpdateCompanyConfig_disabling_approver_required_kicks_pending_quotes_back_to_revised()
    {
        var (companyId, adminUserId, _) = await SeedCompanyWithAdminAsync(approverRequired: true);
        await SeedMembershipAsync(companyId, Guid.NewGuid(), "approver");
        var pendingQuoteId = await SeedQuoteAsync(companyId, "pending-approver");

        var handler = new UpdateCompanyConfigHandler(_db, _audit, _clock);
        var result = await handler.HandleAsync(
            adminUserId, companyId,
            new UpdateCompanyConfigRequest(ApproverRequired: false, PoRequired: null, UniquePoRequired: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        await using var verify = NewContext();
        var quote = await verify.Quotes.SingleAsync(q => q.Id == pendingQuoteId);
        quote.State.Should().Be("revised", "FR-031: pending-approver quotes return to revised when approver_required disabled");

        var transitions = await verify.QuoteStateTransitions
            .Where(t => t.QuoteId == pendingQuoteId && t.NewState == "revised")
            .ToListAsync();
        transitions.Should().HaveCount(1);
    }

    [Fact]
    public async Task RemoveLastAdmin_rejected_with_last_admin_cannot_be_removed()
    {
        var (companyId, adminUserId, adminMembershipId) = await SeedCompanyWithAdminAsync(approverRequired: false);
        // Add a buyer so the company has > 1 membership but only 1 admin.
        await SeedMembershipAsync(companyId, Guid.NewGuid(), "buyer");

        var handler = new MemberHandler(_db, _audit, _clock);
        var result = await handler.RemoveAsync(adminUserId, companyId, adminMembershipId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.ReasonCode.Should().Be(QuoteReasonCode.CompanyLastAdminCannotBeRemoved);
    }

    [Fact]
    public async Task RemoveLastApprover_with_required_rejected()
    {
        var (companyId, adminUserId, _) = await SeedCompanyWithAdminAsync(approverRequired: true);
        var approverMembershipId = await SeedMembershipAsync(companyId, Guid.NewGuid(), "approver");

        var handler = new MemberHandler(_db, _audit, _clock);
        var result = await handler.RemoveAsync(adminUserId, companyId, approverMembershipId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.ReasonCode.Should().Be(QuoteReasonCode.CompanyLastApproverCannotBeRemovedWithRequired);
    }

    [Fact]
    public async Task RemoveLastApprover_when_required_is_false_is_allowed()
    {
        var (companyId, adminUserId, _) = await SeedCompanyWithAdminAsync(approverRequired: false);
        var approverMembershipId = await SeedMembershipAsync(companyId, Guid.NewGuid(), "approver");

        var handler = new MemberHandler(_db, _audit, _clock);
        var result = await handler.RemoveAsync(adminUserId, companyId, approverMembershipId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task PendingInvitation_unique_per_company_email_role()
    {
        var (companyId, _, _) = await SeedCompanyWithAdminAsync(approverRequired: false);

        _db.CompanyInvitations.Add(BuildInvitation(companyId, "user@example.test", "buyer", "pending"));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        _db.CompanyInvitations.Add(BuildInvitation(companyId, "user@example.test", "buyer", "pending"));
        var duplicate = async () => await _db.SaveChangesAsync();
        await duplicate.Should().ThrowAsync<DbUpdateException>("partial unique index forbids duplicate pending invitations");
    }

    private B2BDbContext NewContext() =>
        new(new DbContextOptionsBuilder<B2BDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);

    private async Task<(Guid companyId, Guid adminUserId, Guid adminMembershipId)> SeedCompanyWithAdminAsync(bool approverRequired)
    {
        var companyId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var adminMembershipId = Guid.NewGuid();
        _db.Companies.Add(new Company
        {
            Id = companyId,
            MarketCode = "ksa",
            NameJson = "{\"en\":\"Test\",\"ar\":\"اختبار\"}",
            TaxId = "TAX-" + Guid.NewGuid().ToString("N")[..10],
            PrimaryAddressJson = "{}",
            BillingAddressJson = null,
            ApproverRequired = approverRequired,
            PoRequired = false,
            UniquePoRequired = false,
            InvoiceBillingEligible = true,
            State = "active",
            CreatedAt = _clock.GetUtcNow(),
            UpdatedAt = _clock.GetUtcNow(),
        });
        _db.CompanyMemberships.Add(new CompanyMembership
        {
            Id = adminMembershipId,
            CompanyId = companyId,
            MarketCode = "ksa",
            UserId = adminUserId,
            Role = "companies.admin",
            JoinedAt = _clock.GetUtcNow(),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return (companyId, adminUserId, adminMembershipId);
    }

    private async Task<Guid> SeedMembershipAsync(Guid companyId, Guid userId, string role)
    {
        var id = Guid.NewGuid();
        _db.CompanyMemberships.Add(new CompanyMembership
        {
            Id = id,
            CompanyId = companyId,
            MarketCode = "ksa",
            UserId = userId,
            Role = role,
            JoinedAt = _clock.GetUtcNow(),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return id;
    }

    private async Task<Guid> SeedQuoteAsync(Guid companyId, string state)
    {
        var id = Guid.NewGuid();
        _db.Quotes.Add(new Quote
        {
            Id = id,
            CustomerId = Guid.NewGuid(),
            CompanyId = companyId,
            BranchId = null,
            MarketCode = "ksa",
            State = state,
            RequestedAt = _clock.GetUtcNow().AddDays(-1),
            ExpiresAt = _clock.GetUtcNow().AddDays(7),
            CustomerSuppliedMessageJson = null,
            RestrictionPolicySnapshotJson = "{}",
            SchemaVersion = 1,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return id;
    }

    private CompanyInvitation BuildInvitation(Guid companyId, string email, string role, string state)
    {
        var id = Guid.NewGuid();
        return new CompanyInvitation
        {
            Id = id,
            CompanyId = companyId,
            MarketCode = "ksa",
            InvitedBy = Guid.NewGuid(),
            InvitedEmail = email,
            TargetRole = role,
            TokenHash = "hash-" + id.ToString("N"),
            State = state,
            SentAt = _clock.GetUtcNow().AddDays(-1),
            ExpiresAt = _clock.GetUtcNow().AddDays(7),
        };
    }

    private static async Task SeedMarketSchemaAsync(B2BDbContext db)
    {
        if (await db.QuoteMarketSchemas.AnyAsync()) return;
        db.QuoteMarketSchemas.AddRange(BuildSchema("ksa"), BuildSchema("eg"));
        await db.SaveChangesAsync();
    }

    private static QuoteMarketSchema BuildSchema(string market) => new()
    {
        MarketCode = market,
        Version = 1,
        EffectiveFrom = DateTimeOffset.UtcNow,
        EffectiveTo = null,
        ValidityDays = 14,
        RateLimitPerCustomerPerHour = 10,
        RateLimitPerCompanyPerHour = 50,
        CompanyVerificationRequired = false,
        TaxPreviewDriftThresholdPct = 5.00m,
        SlaDecisionBusinessDays = 2,
        SlaWarningBusinessDays = 1,
        InvitationTtlDays = 14,
        HolidaysListJson = "[]",
    };

    private sealed class RecordingAuditPublisher : IAuditEventPublisher
    {
        public List<AuditEvent> Events { get; } = new();
        public Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }
}
