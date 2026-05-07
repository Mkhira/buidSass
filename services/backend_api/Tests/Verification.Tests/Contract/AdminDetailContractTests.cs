using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Verification.Authorization;
using FluentAssertions;
using Verification.Tests.Contract.Infrastructure;

namespace Verification.Tests.Contract;

/// <summary>
/// Spec 020 T064 — HTTP contract suite for
/// <c>GET /api/admin/verifications/{id}</c> per
/// <see href="../../../../specs/phase-1D/020-verification/contracts/verification-contract.md">contracts §3.2</see>.
///
/// <para>Asserts the schema-as-submitted (FR-026), the full transition history,
/// the document metadata block, and the foreign-market 404.</para>
/// </summary>
[Collection("VerificationContractCollection")]
public sealed class AdminDetailContractTests
{
    private readonly VerificationApiFactory _factory;

    public AdminDetailContractTests(VerificationApiFactory factory) => _factory = factory;

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

    private async Task<Guid> SubmitNewVerificationAsync(Guid customerId, string market = "ksa")
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
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(body.GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task Detail_returns_schema_snapshot_and_transition_history()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitNewVerificationAsync(customerId);

        var reviewerId = Guid.NewGuid();
        using var client = NewAdminClient(reviewerId, VerificationPermissions.Review);

        var resp = await client.GetAsync($"/api/admin/verifications/{verificationId}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetString().Should().Be(verificationId.ToString());
        body.GetProperty("state").GetString().Should().Be("submitted");
        body.GetProperty("marketCode").GetString().Should().Be("ksa");

        // Schema-as-submitted (FR-026): schemaSnapshot block carries the
        // schema row that was active at submission, NOT the current schema.
        var schema = body.GetProperty("schemaSnapshot");
        schema.GetProperty("marketCode").GetString().Should().Be("ksa");
        schema.GetProperty("version").GetInt32().Should().BeGreaterThanOrEqualTo(1);

        // Full transition history — at minimum the initial customer-submission row.
        body.GetProperty("transitions").GetArrayLength().Should().BeGreaterThanOrEqualTo(1);

        // Document metadata block (empty array on a fresh submit, but the
        // contract requires the field to be present).
        body.TryGetProperty("documents", out var docs).Should().BeTrue();
        docs.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Detail_404_when_target_is_in_foreign_market()
    {
        // EG row should not be reachable by a KSA reviewer — 404 (not 403)
        // to avoid leaking row existence across markets.
        await _factory.ResetVerificationAsync();
        var verificationId = await SubmitNewVerificationAsync(Guid.NewGuid(), market: "eg");

        var reviewerId = Guid.NewGuid();
        using var client = NewAdminClient(reviewerId, VerificationPermissions.Review, market: "ksa");

        var resp = await client.GetAsync($"/api/admin/verifications/{verificationId}");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
