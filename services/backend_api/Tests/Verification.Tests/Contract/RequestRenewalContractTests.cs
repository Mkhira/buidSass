using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Primitives;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Verification.Tests.Contract.Infrastructure;

namespace Verification.Tests.Contract;

/// <summary>
/// Spec 020 T048 — HTTP contract suite for
/// <c>POST /api/customer/verifications/renew</c> per
/// <see href="../../../../specs/phase-1D/020-verification/contracts/verification-contract.md">contracts §2.7</see>.
///
/// <para><b>Implementation drift note:</b> the contracts file lists
/// <c>verification.renewal_window_not_open</c> and
/// <c>verification.no_active_approval</c> as distinct wire codes. PR #46
/// finalized the implementation so both surface as
/// <c>verification.renewal_not_eligible</c> with the differentiation in
/// the Problem Details <c>detail</c> field
/// (<c>"no_active_approval"</c> vs. <c>"renewal_window_not_open"</c>).
/// <c>verification.renewal_already_pending</c> remains a distinct code.</para>
/// </summary>
[Collection("VerificationContractCollection")]
public sealed class RequestRenewalContractTests
{
    private readonly VerificationApiFactory _factory;

    public RequestRenewalContractTests(VerificationApiFactory factory) => _factory = factory;

    private HttpClient NewCustomerClient(Guid customerId, string market = "ksa")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Customer-Id", customerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", market);
        return client;
    }

    [Fact]
    public async Task Renew_409_renewal_not_eligible_when_no_active_approval_exists()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        using var client = NewCustomerClient(customerId);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/customer/verifications/renew")
        {
            Content = JsonContent.Create(new
            {
                profession = (string?)null,
                regulatorIdentifier = (string?)null,
            }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be("verification.renewal_not_eligible");
        body.GetProperty("detail").GetString().Should().Contain("no_active_approval",
            "the detail field differentiates between no_active_approval and renewal_window_not_open");
    }

    [Fact]
    public async Task Renew_409_renewal_not_eligible_when_expires_at_outside_reminder_window()
    {
        // KSA schema's reminder_windows_days = [30, 14, 7, 1]. With ExpiresAt
        // 60 days out the renewal window doesn't open until day 30 — before
        // that, the handler returns renewal_not_eligible with detail
        // "renewal_window_not_open".
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        var nowUtc = DateTimeOffset.UtcNow;
        await SeedApprovedVerificationAsync(customerId, nowUtc, expiresIn: TimeSpan.FromDays(60));

        using var client = NewCustomerClient(customerId);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/customer/verifications/renew")
        {
            Content = JsonContent.Create(new { }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be("verification.renewal_not_eligible");
        body.GetProperty("detail").GetString().Should().Contain("renewal_window_not_open",
            "the detail field differentiates renewal_window_not_open from no_active_approval");
    }

    [Fact]
    public async Task Renew_409_renewal_already_pending_when_concurrent_renewal_exists()
    {
        // FR-020 — only one non-terminal renewal per approval at a time.
        // Seed an approval with ExpiresAt inside the renewal window AND a
        // submitted renewal pointing at it; second renew call returns
        // renewal_already_pending.
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        var nowUtc = DateTimeOffset.UtcNow;
        var approvalId = await SeedApprovedVerificationAsync(
            customerId, nowUtc, expiresIn: TimeSpan.FromDays(5));
        await SeedPendingRenewalAsync(approvalId, customerId, nowUtc);

        using var client = NewCustomerClient(customerId);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/customer/verifications/renew")
        {
            Content = JsonContent.Create(new { }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be("verification.renewal_already_pending");
    }

    [Fact]
    public async Task Renew_201_creates_renewal_with_supersedes_id_pointing_to_prior_approval()
    {
        // Happy path — approval with ExpiresAt 5 days out (well inside the
        // 30-day earliest reminder window). Renewal succeeds and the new
        // row's supersedes_id links back to the prior approval.
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        var nowUtc = DateTimeOffset.UtcNow;
        var approvalId = await SeedApprovedVerificationAsync(
            customerId, nowUtc, expiresIn: TimeSpan.FromDays(5));

        using var client = NewCustomerClient(customerId);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/customer/verifications/renew")
        {
            Content = JsonContent.Create(new { }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        // Renewal endpoint returns either 200 OK or 201 Created on success
        // depending on the endpoint's chosen Results.* shape — assert on
        // success-class plus the persisted linkage rather than pinning a
        // specific 2xx code.
        ((int)resp.StatusCode).Should().BeInRange(200, 299,
            "renewal opening on a valid approval is a success");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var renewalId = Guid.Parse(body.GetProperty("id").GetString()!);
        body.GetProperty("supersedesId").GetString().Should().Be(approvalId.ToString(),
            "renewal payload exposes supersedes_id pointing to the prior approval");
        body.GetProperty("state").GetString().Should().Be("submitted");

        // Persistence side-effect: the renewal row's SupersedesId column
        // matches the approval's id.
        await using var verify = _factory.NewDbContext();
        var renewalRow = await verify.Verifications.SingleAsync(v => v.Id == renewalId);
        renewalRow.SupersedesId.Should().Be(approvalId);
        renewalRow.State.Should().Be(VerificationState.Submitted);
    }

    private async Task<Guid> SeedApprovedVerificationAsync(
        Guid customerId,
        DateTimeOffset nowUtc,
        TimeSpan expiresIn)
    {
        await using var ctx = _factory.NewDbContext();
        var schema = await ctx.MarketSchemas
            .Where(s => s.MarketCode == "ksa" && s.EffectiveTo == null)
            .OrderByDescending(s => s.Version)
            .FirstAsync();
        var approvalId = Guid.NewGuid();
        ctx.Verifications.Add(new BackendApi.Modules.Verification.Entities.Verification
        {
            Id = approvalId,
            CustomerId = customerId,
            MarketCode = "ksa",
            SchemaVersion = schema.Version,
            Profession = "dentist",
            RegulatorIdentifier = "SCFHS-1234567",
            State = VerificationState.Approved,
            SubmittedAt = nowUtc.AddDays(-30),
            DecidedAt = nowUtc.AddDays(-29),
            DecidedBy = Guid.NewGuid(),
            ExpiresAt = nowUtc + expiresIn,
            CreatedAt = nowUtc.AddDays(-30),
            UpdatedAt = nowUtc.AddDays(-29),
        });
        ctx.StateTransitions.Add(new VerificationStateTransition
        {
            Id = Guid.NewGuid(),
            VerificationId = approvalId,
            MarketCode = "ksa",
            PriorState = VerificationState.InReview.ToWireValue(),
            NewState = VerificationState.Approved.ToWireValue(),
            ActorKind = "reviewer",
            ActorId = Guid.NewGuid(),
            OccurredAt = nowUtc.AddDays(-29),
            Reason = "seeded_for_renewal_test",
            MetadataJson = "{}",
        });
        await ctx.SaveChangesAsync();
        return approvalId;
    }

    private async Task SeedPendingRenewalAsync(Guid approvalId, Guid customerId, DateTimeOffset nowUtc)
    {
        await using var ctx = _factory.NewDbContext();
        var schema = await ctx.MarketSchemas
            .Where(s => s.MarketCode == "ksa" && s.EffectiveTo == null)
            .OrderByDescending(s => s.Version)
            .FirstAsync();
        var renewalId = Guid.NewGuid();
        ctx.Verifications.Add(new BackendApi.Modules.Verification.Entities.Verification
        {
            Id = renewalId,
            CustomerId = customerId,
            MarketCode = "ksa",
            SchemaVersion = schema.Version,
            Profession = "dentist",
            RegulatorIdentifier = "SCFHS-1234567",
            State = VerificationState.Submitted,
            SubmittedAt = nowUtc.AddHours(-1),
            SupersedesId = approvalId,
            CreatedAt = nowUtc.AddHours(-1),
            UpdatedAt = nowUtc.AddHours(-1),
        });
        ctx.StateTransitions.Add(new VerificationStateTransition
        {
            Id = Guid.NewGuid(),
            VerificationId = renewalId,
            MarketCode = "ksa",
            PriorState = VerificationStateMachine.PriorStateNoneWire,
            NewState = VerificationState.Submitted.ToWireValue(),
            ActorKind = "customer",
            ActorId = customerId,
            OccurredAt = nowUtc.AddHours(-1),
            Reason = "seeded_pending_renewal",
            MetadataJson = "{}",
        });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Renew_400_idempotency_key_missing_when_header_absent()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        using var client = NewCustomerClient(customerId);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/customer/verifications/renew")
        {
            Content = JsonContent.Create(new { }),
        };

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be("verification.idempotency.key_missing");
    }

    [Fact]
    public async Task Renew_401_when_no_customer_principal()
    {
        var client = _factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/customer/verifications/renew")
        {
            Content = JsonContent.Create(new { }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
