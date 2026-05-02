using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Verification.Tests.Contract.Infrastructure;

namespace Verification.Tests.Contract;

/// <summary>
/// Spec 020 T045 — HTTP contract suite for
/// <c>POST /api/customer/verifications/{id}/documents</c> per
/// <see href="../../../../specs/phase-1D/020-verification/contracts/verification-contract.md">contracts §2.5</see>.
///
/// <para><b>Implementation drift note:</b> the contracts file describes
/// multipart/form-data + server-side AV scanning. PR #46 finalized a
/// pre-uploaded JSON contract — the storage object is uploaded out-of-band
/// to spec 015's storage abstraction (which runs the AV scan asynchronously),
/// and the customer hands the resulting <c>storage_key</c> + scan status to
/// this endpoint. The reason codes the contract names map to wire slugs
/// the implementation actually emits as follows:</para>
/// <list type="bullet">
///   <item><c>document_too_large</c> → <c>verification.document.size_exceeded</c></item>
///   <item><c>document_type_not_allowed</c> → <c>verification.document.mime_forbidden</c></item>
///   <item><c>document_aggregate_exceeded</c> → <c>verification.document.aggregate_size_exceeded</c></item>
///   <item><c>document_scan_failed</c> → <c>verification.document.scan_infected</c> (scan status <c>infected</c> at attach time)</item>
///   <item><c>invalid_state_for_action</c> → <c>verification.invalid_state_for_action</c></item>
/// </list>
/// </summary>
[Collection("VerificationContractCollection")]
public sealed class AttachDocumentContractTests
{
    private readonly VerificationApiFactory _factory;

    public AttachDocumentContractTests(VerificationApiFactory factory) => _factory = factory;

    private HttpClient NewCustomerClient(Guid customerId, string market = "ksa")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Customer-Id", customerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", market);
        return client;
    }

    private async Task<Guid> SubmitNewVerificationAsync(HttpClient client, Guid customerId)
    {
        // Submit a fresh verification so the attach path has a parent row.
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/customer/verifications/")
        {
            Content = JsonContent.Create(new
            {
                profession = "dentist",
                regulatorIdentifier = "SCFHS-1234567",
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
    public async Task Attach_201_clean_document_persists_with_scan_status_clean()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        using var client = NewCustomerClient(customerId);
        var verificationId = await SubmitNewVerificationAsync(client, customerId);

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/verifications/{verificationId}/documents")
        {
            Content = JsonContent.Create(new
            {
                storageKey = "spec-015/clean-license.png",
                contentType = "image/png",
                sizeBytes = 1_500_000L,
                scanStatus = "clean",
            }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("scanStatus").GetString().Should().Be("clean");
        body.GetProperty("contentType").GetString().Should().Be("image/png");
        body.GetProperty("verificationId").GetString().Should().Be(verificationId.ToString());
    }

    [Fact]
    public async Task Attach_400_size_exceeded_when_document_over_10MB()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        using var client = NewCustomerClient(customerId);
        var verificationId = await SubmitNewVerificationAsync(client, customerId);

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/verifications/{verificationId}/documents")
        {
            Content = JsonContent.Create(new
            {
                storageKey = "spec-015/oversized.pdf",
                contentType = "application/pdf",
                sizeBytes = 12L * 1024 * 1024,   // 12 MB > 10 MB cap
                scanStatus = "clean",
            }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertProblemAsync(resp, "verification.document.size_exceeded");
    }

    [Fact]
    public async Task Attach_400_mime_forbidden_when_content_type_not_in_allowlist()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        using var client = NewCustomerClient(customerId);
        var verificationId = await SubmitNewVerificationAsync(client, customerId);

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/verifications/{verificationId}/documents")
        {
            Content = JsonContent.Create(new
            {
                storageKey = "spec-015/license.exe",
                contentType = "application/x-msdownload",     // not in any market schema's allowlist
                sizeBytes = 100_000L,
                scanStatus = "clean",
            }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertProblemAsync(resp, "verification.document.mime_forbidden");
    }

    [Fact]
    public async Task Attach_400_scan_infected_when_upstream_marked_infected()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        using var client = NewCustomerClient(customerId);
        var verificationId = await SubmitNewVerificationAsync(client, customerId);

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/verifications/{verificationId}/documents")
        {
            Content = JsonContent.Create(new
            {
                storageKey = "spec-015/infected.pdf",
                contentType = "application/pdf",
                sizeBytes = 500_000L,
                scanStatus = "infected",   // upstream AV scanner returned infected
            }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertProblemAsync(resp, "verification.document.scan_infected");
    }

    [Fact]
    public async Task Attach_404_when_verification_not_owned_by_caller()
    {
        await _factory.ResetVerificationAsync();
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        using var clientA = NewCustomerClient(customerA);
        using var clientB = NewCustomerClient(customerB);

        var verificationId = await SubmitNewVerificationAsync(clientA, customerA);

        // Customer B tries to attach to A's verification → 404 (not 403, to
        // avoid leaking row existence).
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/verifications/{verificationId}/documents")
        {
            Content = JsonContent.Create(new
            {
                storageKey = "spec-015/foreign.pdf",
                contentType = "application/pdf",
                sizeBytes = 100_000L,
                scanStatus = "clean",
            }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await clientB.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertProblemAsync(resp, "verification.not_found");
    }

    [Fact]
    public async Task Attach_400_idempotency_key_missing_when_header_absent()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        using var client = NewCustomerClient(customerId);
        var verificationId = await SubmitNewVerificationAsync(client, customerId);

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/verifications/{verificationId}/documents")
        {
            Content = JsonContent.Create(new
            {
                storageKey = "spec-015/license.png",
                contentType = "image/png",
                sizeBytes = 500_000L,
                scanStatus = "clean",
            }),
        };

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertProblemAsync(resp, "verification.idempotency.key_missing");
    }

    private static async Task AssertProblemAsync(HttpResponseMessage resp, string expectedReasonCode)
    {
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be(expectedReasonCode);
    }
}
