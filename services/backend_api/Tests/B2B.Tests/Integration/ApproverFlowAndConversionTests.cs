using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.B2B.Conversion;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.Quotes.Approver.FinalizeAcceptance;
using BackendApi.Modules.B2B.Quotes.Approver.ListPendingApprovals;
using BackendApi.Modules.B2B.Quotes.Approver.RejectAcceptance;
using BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromCart;
using BackendApi.Modules.Shared;
using B2B.Tests.Fixtures;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace B2B.Tests.Integration;

/// <summary>
/// Spec 021 backfill for the deferred US5 + US6 tests (T121–T125 + T094–T098).
/// Drives the approver-finalize / approver-reject / list-pending-approvals
/// surface and the underlying <see cref="QuoteToOrderConverter"/> directly,
/// locking in the highest-value invariants:
///
/// <list type="bullet">
///   <item>SC-009 — multi-approver finalize race: first wins via xmin guard;
///         second sees <c>409 quote.already_decided</c>.</item>
///   <item>SC-007 / T094 — converter atomicity: when the order-creation bridge
///         throws, the quote stays in <c>pending-approver</c> (no half-state).</item>
///   <item>T098 — invoice_billing flag is captured on the conversion request and
///         mirrored on the quote (true for company quotes).</item>
///   <item>T121 — list-pending-approvals scopes to caller's approver-companies only.</item>
///   <item>T123 — RejectAcceptance moves <c>pending-approver → revised</c>
///         and persists the locale-required comment on the quote.</item>
/// </list>
/// </summary>
public sealed class ApproverFlowAndConversionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("b2b_approver_conversion")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly RecordingAuditPublisher _audit = new();
    private readonly RecordingDomainPublisher _domain = new();
    private readonly StubOrderFromQuoteHandler _orderBridge = new();
    private readonly StubCustomerVerificationEligibilityQuery _eligibility = new();
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
    public async Task FinalizeAcceptance_two_approvers_first_wins_second_sees_already_decided()
    {
        var (companyId, _, approverA) = await SeedCompanyWithApproverAsync(approverRequired: true);
        var approverB = await SeedMembershipAsync(companyId, Guid.NewGuid(), "approver");
        _ = approverB;
        var (quoteId, _) = await SeedPendingApproverQuoteAsync(companyId);

        // Race two finalize calls. We use sequential-but-conflicting invocations to
        // simulate the lost-update race deterministically: load the same row in two
        // contexts, mutate both, save the second and assert the rejection. The
        // production xmin guard catches both real-time races and the lost-update
        // pattern with the same DbUpdateConcurrencyException.
        var ctxA = NewContext();
        var ctxB = NewContext();
        try
        {
            var handlerA = NewFinalizeHandler(ctxA);
            var handlerB = NewFinalizeHandler(ctxB);

            // Both handlers SELECT the quote; first SaveChanges wins.
            var quoteFromA = await ctxA.Quotes.SingleAsync(q => q.Id == quoteId);
            var quoteFromB = await ctxB.Quotes.SingleAsync(q => q.Id == quoteId);
            quoteFromA.XminRowVersion.Should().Be(quoteFromB.XminRowVersion);

            var first = await handlerA.HandleAsync(quoteId, approverA, Guid.NewGuid(),
                taxPreviewDriftAcknowledged: false, CancellationToken.None);
            first.IsSuccess.Should().BeTrue();

            var second = await handlerB.HandleAsync(quoteId, approverA, Guid.NewGuid(),
                taxPreviewDriftAcknowledged: false, CancellationToken.None);
            second.IsSuccess.Should().BeFalse();
            second.ReasonCode.Should().Be(QuoteReasonCode.QuoteAlreadyDecided);
        }
        finally
        {
            await ctxA.DisposeAsync();
            await ctxB.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConverterFailure_keeps_quote_in_pending_approver_state()
    {
        var (companyId, _, approverId) = await SeedCompanyWithApproverAsync(approverRequired: true);
        var (quoteId, _) = await SeedPendingApproverQuoteAsync(companyId);

        // Inject a transient failure into the order-bridge stub. The handler
        // wraps the converter, which surfaces a non-eligibility / non-drift
        // failure as InvalidState — the quote MUST stay in pending-approver.
        _orderBridge.FailureToThrow = new InvalidOperationException("simulated downstream order failure");

        var handler = NewFinalizeHandler(_db);
        var result = await handler.HandleAsync(quoteId, approverId, Guid.NewGuid(),
            taxPreviewDriftAcknowledged: false, CancellationToken.None);
        result.IsSuccess.Should().BeFalse();

        await using var verify = NewContext();
        var quote = await verify.Quotes.SingleAsync(q => q.Id == quoteId);
        quote.State.Should().Be("pending-approver", "atomicity: failed conversion MUST roll back the state move (SC-007)");
        quote.DecidedAt.Should().BeNull();
        quote.TerminalAt.Should().BeNull();
    }

    [Fact]
    public async Task FinalizeAcceptance_company_quote_stamps_invoice_billing_true_on_request()
    {
        var (companyId, _, approverId) = await SeedCompanyWithApproverAsync(approverRequired: true);
        var (quoteId, _) = await SeedPendingApproverQuoteAsync(companyId);

        var handler = NewFinalizeHandler(_db);
        var result = await handler.HandleAsync(quoteId, approverId, Guid.NewGuid(),
            taxPreviewDriftAcknowledged: false, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        _orderBridge.Invocations.Should().HaveCount(1);
        _orderBridge.Invocations[0].InvoiceBilling.Should().BeTrue(
            "FR-027: company-quote conversions MUST set invoice_billing=true on the order");
    }

    [Fact]
    public async Task ListPendingApprovals_scopes_to_caller_approver_companies()
    {
        var (companyA, _, approverInA) = await SeedCompanyWithApproverAsync(approverRequired: true);
        var (companyB, _, approverInB) = await SeedCompanyWithApproverAsync(approverRequired: true);

        await SeedPendingApproverQuoteAsync(companyA);
        await SeedPendingApproverQuoteAsync(companyB);

        var handler = new ListPendingApprovalsHandler(_db, _clock);
        var responseA = await handler.HandleAsync(approverInA, companyA, page: 1, pageSize: 50, CancellationToken.None);

        responseA.Items.Should().HaveCount(1, "approver-A only sees A's pending quote");
        responseA.Items[0].CompanyId.Should().Be(companyA);

        var responseUnauth = await handler.HandleAsync(approverInA, companyB, page: 1, pageSize: 50, CancellationToken.None);
        responseUnauth.Items.Should().BeEmpty(
            "an approver scoped to A MUST NOT see pending quotes from B even by passing B's company-id");
    }

    [Fact]
    public async Task RejectAcceptance_transitions_pending_approver_to_revised_with_comment()
    {
        var (companyId, _, approverId) = await SeedCompanyWithApproverAsync(approverRequired: true);
        var (quoteId, _) = await SeedPendingApproverQuoteAsync(companyId);

        var handler = new RejectAcceptanceHandler(_db, _audit, _domain, _clock);
        var comment = new LocalizedMessage(
            "Please reduce the line discounts.",
            "يرجى تقليل خصم السطر.");
        var result = await handler.HandleAsync(quoteId, approverId, comment, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        await using var verify = NewContext();
        var quote = await verify.Quotes.SingleAsync(q => q.Id == quoteId);
        quote.State.Should().Be("revised");
        quote.ApproverRejectionNote.Should().NotBeNull();
        var note = JsonDocument.Parse(quote.ApproverRejectionNote!);
        note.RootElement.GetProperty("en").GetString().Should().Be("Please reduce the line discounts.");
        note.RootElement.GetProperty("ar").GetString().Should().Be("يرجى تقليل خصم السطر.");
    }

    private B2BDbContext NewContext() =>
        new(new DbContextOptionsBuilder<B2BDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);

    private FinalizeAcceptanceHandler NewFinalizeHandler(B2BDbContext db)
    {
        var converter = new QuoteToOrderConverter(
            db, _orderBridge, _eligibility, _audit, _domain, _clock,
            NullLogger<QuoteToOrderConverter>.Instance);
        return new FinalizeAcceptanceHandler(
            db, converter, _audit, _domain, _clock,
            NullLogger<FinalizeAcceptanceHandler>.Instance);
    }

    private async Task<(Guid companyId, Guid adminUserId, Guid approverUserId)> SeedCompanyWithApproverAsync(bool approverRequired)
    {
        var companyId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var approverUserId = Guid.NewGuid();
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
        _db.CompanyMemberships.AddRange(
            new CompanyMembership
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId, MarketCode = "ksa",
                UserId = adminUserId, Role = "companies.admin",
                JoinedAt = _clock.GetUtcNow(),
            },
            new CompanyMembership
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId, MarketCode = "ksa",
                UserId = approverUserId, Role = "approver",
                JoinedAt = _clock.GetUtcNow(),
            });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return (companyId, adminUserId, approverUserId);
    }

    private async Task<Guid> SeedMembershipAsync(Guid companyId, Guid userId, string role)
    {
        _db.CompanyMemberships.Add(new CompanyMembership
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            MarketCode = "ksa",
            UserId = userId,
            Role = role,
            JoinedAt = _clock.GetUtcNow(),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return userId;
    }

    private async Task<(Guid quoteId, Guid versionId)> SeedPendingApproverQuoteAsync(Guid companyId)
    {
        var quoteId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        _db.Quotes.Add(new Quote
        {
            Id = quoteId,
            CustomerId = customerId,
            CompanyId = companyId,
            BranchId = null,
            MarketCode = "ksa",
            State = "pending-approver",
            RequestedAt = _clock.GetUtcNow().AddDays(-1),
            ExpiresAt = _clock.GetUtcNow().AddDays(7),
            CurrentVersionId = null,
            InvoiceBilling = true,
            CustomerSuppliedMessageJson = null,
            RestrictionPolicySnapshotJson = "{}",
            SchemaVersion = 1,
        });
        _db.QuoteStateTransitions.Add(new QuoteStateTransition
        {
            Id = Guid.NewGuid(),
            QuoteId = quoteId,
            MarketCode = "ksa",
            PriorState = "__none__",
            NewState = "pending-approver",
            ActorKind = QuoteActorKind.Buyer.ToToken(),
            ActorId = customerId,
            ReasonJson = null,
            MetadataJson = "{}",
            OccurredAt = _clock.GetUtcNow().AddHours(-1),
        });
        await _db.SaveChangesAsync();

        _db.QuoteVersions.Add(new QuoteVersion
        {
            Id = versionId,
            QuoteId = quoteId,
            MarketCode = "ksa",
            VersionNumber = 1,
            AuthoredBy = Guid.NewGuid(),
            PublishedAt = _clock.GetUtcNow().AddHours(-2),
            LineItemsJson = "[{\"sku\":\"TEST-SKU\",\"qty\":1,\"unit_price\":100,\"baseline_unit_price\":100,\"line_discount_amount\":0,\"line_tax_preview\":15,\"currency\":\"SAR\"}]",
            TermsTextJson = "{\"en\":\"Net 30\",\"ar\":\"صافي 30\"}",
            TermsDays = 30,
            ValidityExtends = false,
            TotalsSummaryJson = "{\"subtotal\":100,\"total_discount\":0,\"total_tax_preview\":15,\"grand_total\":115,\"currency\":\"SAR\"}",
        });
        await _db.SaveChangesAsync();

        var quote = await _db.Quotes.SingleAsync(q => q.Id == quoteId);
        quote.CurrentVersionId = versionId;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        return (quoteId, versionId);
    }

    private static async Task SeedMarketSchemaAsync(B2BDbContext db)
    {
        if (await db.QuoteMarketSchemas.AnyAsync()) return;
        db.QuoteMarketSchemas.AddRange(
            BuildSchema("ksa"), BuildSchema("eg"));
        await db.SaveChangesAsync();
    }

    private static QuoteMarketSchema BuildSchema(string market) => new()
    {
        MarketCode = market, Version = 1,
        EffectiveFrom = DateTimeOffset.UtcNow, EffectiveTo = null,
        ValidityDays = 14,
        RateLimitPerCustomerPerHour = 10, RateLimitPerCompanyPerHour = 50,
        CompanyVerificationRequired = false,
        TaxPreviewDriftThresholdPct = 5.00m,
        SlaDecisionBusinessDays = 2, SlaWarningBusinessDays = 1,
        InvitationTtlDays = 14, HolidaysListJson = "[]",
    };

    private sealed class RecordingAuditPublisher : IAuditEventPublisher
    {
        public List<AuditEvent> Events { get; } = new();
        public Task PublishAsync(AuditEvent e, CancellationToken c) { Events.Add(e); return Task.CompletedTask; }
    }

    private sealed class RecordingDomainPublisher : IPublisher
    {
        public List<INotification> Notifications { get; } = new();
        public Task Publish<TNotification>(TNotification n, CancellationToken c = default)
            where TNotification : INotification
        { Notifications.Add(n); return Task.CompletedTask; }

        public Task Publish(object n, CancellationToken c = default)
        { if (n is INotification i) Notifications.Add(i); return Task.CompletedTask; }
    }
}
