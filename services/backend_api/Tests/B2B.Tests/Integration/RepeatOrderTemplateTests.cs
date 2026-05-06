using BackendApi.Modules.AuditLog;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromCart;
using BackendApi.Modules.B2B.Quotes.Customer.SaveAsRepeatOrderTemplate;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace B2B.Tests.Integration;

/// <summary>
/// Spec 021 backfill for the deferred US7 tests (T131, T132). Drives
/// <see cref="SaveAsRepeatOrderTemplateHandler"/> directly to lock in the two
/// invariants the storage layer enforces:
///
/// <list type="bullet">
///   <item>Only quotes in <c>accepted</c> state can spawn a template.</item>
///   <item>Per [research §R12](./research.md), a name is unique per
///         <c>(company_id, name)</c> for company-scoped templates and
///         <c>(user_id, name)</c> for individual ones — second insert with the
///         same scope hits the partial unique index → 409 template.name_already_exists.</item>
///   <item>Different scopes (different company OR different user) MAY reuse the
///         same template name without conflict.</item>
/// </list>
/// </summary>
public sealed class RepeatOrderTemplateTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("b2b_repeat_order_template")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly RecordingAuditPublisher _audit = new();
    private B2BDbContext _db = default!;
    private SaveAsRepeatOrderTemplateHandler _handler = default!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _db = NewContext();
        await _db.Database.MigrateAsync();
        _handler = new SaveAsRepeatOrderTemplateHandler(_db, _audit, _clock,
            NullLogger<SaveAsRepeatOrderTemplateHandler>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Saves_template_for_accepted_quote()
    {
        var customerId = Guid.NewGuid();
        var quoteId = await SeedAcceptedQuoteAsync(customerId);

        var result = await _handler.HandleAsync(
            customerId, quoteId,
            new SaveAsRepeatOrderTemplateRequest(new LocalizedMessage("Monthly", "شهري")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);

        await using var verify = NewContext();
        var template = await verify.RepeatOrderTemplates.SingleAsync();
        template.SourceQuoteId.Should().Be(quoteId);
        template.UserId.Should().Be(customerId);
    }

    [Fact]
    public async Task Rejects_when_quote_not_yet_accepted()
    {
        var customerId = Guid.NewGuid();
        var quoteId = await SeedQuoteAsync(customerId, state: "revised");

        var result = await _handler.HandleAsync(
            customerId, quoteId,
            new SaveAsRepeatOrderTemplateRequest(new LocalizedMessage("Monthly", "شهري")),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.ReasonCode.Should().Be(BackendApi.Modules.B2B.Primitives.QuoteReasonCode.QuoteInvalidStateForAction);
    }

    [Fact]
    public async Task Returns_name_already_exists_on_duplicate_within_same_scope()
    {
        var customerId = Guid.NewGuid();
        var quoteId = await SeedAcceptedQuoteAsync(customerId);

        var first = await _handler.HandleAsync(
            customerId, quoteId,
            new SaveAsRepeatOrderTemplateRequest(new LocalizedMessage("Monthly", "شهري")),
            CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        var second = await _handler.HandleAsync(
            customerId, quoteId,
            new SaveAsRepeatOrderTemplateRequest(new LocalizedMessage("Monthly", "شهري")),
            CancellationToken.None);
        second.IsSuccess.Should().BeFalse();
        second.ReasonCode.Should().Be(BackendApi.Modules.B2B.Primitives.QuoteReasonCode.TemplateNameAlreadyExists);
    }

    [Fact]
    public async Task Different_users_may_reuse_the_same_name()
    {
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var quoteA = await SeedAcceptedQuoteAsync(customerA);
        var quoteB = await SeedAcceptedQuoteAsync(customerB);

        var firstA = await _handler.HandleAsync(
            customerA, quoteA,
            new SaveAsRepeatOrderTemplateRequest(new LocalizedMessage("Shared Name", "اسم مشترك")),
            CancellationToken.None);
        firstA.IsSuccess.Should().BeTrue();

        var firstB = await _handler.HandleAsync(
            customerB, quoteB,
            new SaveAsRepeatOrderTemplateRequest(new LocalizedMessage("Shared Name", "اسم مشترك")),
            CancellationToken.None);
        firstB.IsSuccess.Should().BeTrue("template uniqueness scope is per-user, not global");
    }

    private B2BDbContext NewContext() =>
        new(new DbContextOptionsBuilder<B2BDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);

    private Task<Guid> SeedAcceptedQuoteAsync(Guid customerId) =>
        SeedQuoteAsync(customerId, state: "accepted");

    private async Task<Guid> SeedQuoteAsync(Guid customerId, string state)
    {
        var id = Guid.NewGuid();
        var nowUtc = _clock.GetUtcNow();
        _db.Quotes.Add(new Quote
        {
            Id = id,
            CustomerId = customerId,
            CompanyId = null,
            BranchId = null,
            MarketCode = "ksa",
            State = state,
            RequestedAt = nowUtc.AddDays(-1),
            ExpiresAt = nowUtc.AddDays(7),
            CustomerSuppliedMessageJson = null,
            RestrictionPolicySnapshotJson = "{}",
            SchemaVersion = 1,
            TerminalAt = state == "accepted" ? nowUtc : null,
            TerminalReason = state == "accepted" ? "accepted" : null,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return id;
    }

    private sealed class RecordingAuditPublisher : IAuditEventPublisher
    {
        public List<AuditEvent> Events { get; } = new();
        public Task PublishAsync(AuditEvent e, CancellationToken c) { Events.Add(e); return Task.CompletedTask; }
    }
}
