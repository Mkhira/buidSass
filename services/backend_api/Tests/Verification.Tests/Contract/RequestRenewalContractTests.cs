using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
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
