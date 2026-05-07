using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Verification.Authorization;
using FluentAssertions;
using Verification.Tests.Contract.Infrastructure;

namespace Verification.Tests.Contract;

/// <summary>
/// Spec 020 T063 — HTTP contract suite for
/// <c>GET /api/admin/verifications</c> per
/// <see href="../../../../specs/phase-1D/020-verification/contracts/verification-contract.md">contracts §3.1</see>.
///
/// <para>Asserts the wire-level contract for the reviewer queue:</para>
/// <list type="bullet">
///   <item>RBAC — 403 with reason code <c>verification.review_permission_required</c>
///         when the caller lacks <c>verification.review</c>.</item>
///   <item>Market scope — KSA-only reviewer sees zero EG rows even when
///         EG verifications exist (Principle 5 / ADR-010 partitioning).</item>
///   <item>Default sort is oldest-first per §3.1.</item>
///   <item>SLA signal renders one of <c>ok | warning | breach</c> on every row.</item>
/// </list>
/// </summary>
[Collection("VerificationContractCollection")]
public sealed class AdminQueueContractTests
{
    private readonly VerificationApiFactory _factory;

    public AdminQueueContractTests(VerificationApiFactory factory) => _factory = factory;

    private HttpClient NewAdminClient(Guid reviewerId, string permissions, string market = "ksa")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Admin-Id", reviewerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        client.DefaultRequestHeaders.Add("X-Test-Market", market);
        return client;
    }

    private HttpClient NewCustomerClient(Guid customerId, string market = "ksa")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Customer-Id", customerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", market);
        return client;
    }

    private async Task SubmitNewVerificationAsync(Guid customerId, string market = "ksa")
    {
        using var client = NewCustomerClient(customerId, market);
        var regulator = market == "ksa" ? "SCFHS-1234567" : "EMS-789-456-1";
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/customer/verifications/")
        {
            Content = JsonContent.Create(new
            {
                profession = "dentist",
                regulatorIdentifier = regulator,
                documentIds = Array.Empty<Guid>(),
            }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Queue_403_when_reviewer_lacks_review_permission()
    {
        await _factory.ResetVerificationAsync();
        var reviewerId = Guid.NewGuid();
        // Empty permissions — RBAC denies.
        using var client = NewAdminClient(reviewerId, permissions: "verification.revoke");

        var resp = await client.GetAsync("/api/admin/verifications/");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be("verification.review_permission_required");
    }

    [Fact]
    public async Task Queue_market_scope_excludes_foreign_market_rows()
    {
        await _factory.ResetVerificationAsync();
        await SubmitNewVerificationAsync(Guid.NewGuid(), market: "ksa");
        await SubmitNewVerificationAsync(Guid.NewGuid(), market: "eg");

        var reviewerId = Guid.NewGuid();
        using var client = NewAdminClient(reviewerId, VerificationPermissions.Review, market: "ksa");

        var resp = await client.GetAsync("/api/admin/verifications/");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items");
        items.GetArrayLength().Should().Be(1, "KSA-only reviewer must NOT see EG rows");
        items[0].GetProperty("marketCode").GetString().Should().Be("ksa");
    }

    [Fact]
    public async Task Queue_default_sort_is_oldest_first_and_renders_sla_signal()
    {
        await _factory.ResetVerificationAsync();
        await SubmitNewVerificationAsync(Guid.NewGuid(), market: "ksa");
        await Task.Delay(20);
        await SubmitNewVerificationAsync(Guid.NewGuid(), market: "ksa");

        var reviewerId = Guid.NewGuid();
        using var client = NewAdminClient(reviewerId, VerificationPermissions.Review);

        var resp = await client.GetAsync("/api/admin/verifications/");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);

        // Oldest-first — first row's submittedAt ≤ second's.
        var first = items[0].GetProperty("submittedAt").GetDateTimeOffset();
        var second = items[1].GetProperty("submittedAt").GetDateTimeOffset();
        first.Should().BeOnOrBefore(second);

        // SLA signal contract — each row carries one of ok | warning | breach.
        foreach (var row in items.EnumerateArray())
        {
            row.GetProperty("slaSignal").GetString()
                .Should().BeOneOf("ok", "warning", "breach");
        }
    }
}
