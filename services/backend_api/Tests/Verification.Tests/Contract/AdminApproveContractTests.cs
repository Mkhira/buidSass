using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Verification.Authorization;
using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Primitives;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Verification.Tests.Contract.Infrastructure;

namespace Verification.Tests.Contract;

/// <summary>
/// Spec 020 T065 — HTTP contract suite for
/// <c>POST /api/admin/verifications/{id}/approve</c> per
/// <see href="../../../../specs/phase-1D/020-verification/contracts/verification-contract.md">contracts §3.3</see>.
///
/// <para>Covers: 200 OK on happy path with bilingual reason; 400
/// <c>verification.review.reason_required</c> when both locales blank;
/// state moves to <c>approved</c>; <c>expires_at</c> is set; row's wire
/// shape includes the audit-log-friendly fields.</para>
/// </summary>
[Collection("VerificationContractCollection")]
public sealed class AdminApproveContractTests
{
    private readonly VerificationApiFactory _factory;

    public AdminApproveContractTests(VerificationApiFactory factory) => _factory = factory;

    private HttpClient NewAdminClient(Guid reviewerId, string permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Admin-Id", reviewerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        client.DefaultRequestHeaders.Add("X-Test-Market", "ksa");
        return client;
    }

    private HttpClient NewCustomerClient(Guid customerId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Customer-Id", customerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", "ksa");
        return client;
    }

    private async Task<Guid> SubmitNewVerificationAsync(Guid customerId)
    {
        using var client = NewCustomerClient(customerId);
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
    public async Task Approve_200_OK_on_happy_path_with_bilingual_reason()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitNewVerificationAsync(customerId);

        var reviewerId = Guid.NewGuid();
        using var client = NewAdminClient(reviewerId, VerificationPermissions.Review);
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/verifications/{verificationId}/approve")
        {
            Content = JsonContent.Create(new
            {
                reason = new
                {
                    en = "License verified against SCFHS register.",
                    ar = "تم التحقق من الترخيص مقابل سجل الهيئة.",
                },
            }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("state").GetString().Should().Be("approved");
        body.GetProperty("id").GetString().Should().Be(verificationId.ToString());

        // Persistence side-effects: row state moves to approved with expires_at set.
        await using var db = _factory.NewDbContext();
        var row = await db.Verifications.AsNoTracking()
            .SingleAsync(v => v.Id == verificationId);
        row.State.Should().Be(VerificationState.Approved);
        row.ExpiresAt.Should().NotBeNull("§3.3 mandates expires_at set on approval");
        row.DecidedBy.Should().Be(reviewerId);
    }

    [Fact]
    public async Task Approve_400_reason_required_when_both_locales_blank()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitNewVerificationAsync(customerId);

        var reviewerId = Guid.NewGuid();
        using var client = NewAdminClient(reviewerId, VerificationPermissions.Review);
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/verifications/{verificationId}/approve")
        {
            Content = JsonContent.Create(new
            {
                reason = new { en = (string?)null, ar = (string?)null },
            }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be("verification.review.reason_required");
    }

    [Fact]
    public async Task Approve_400_idempotency_key_missing_when_header_absent()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitNewVerificationAsync(customerId);

        var reviewerId = Guid.NewGuid();
        using var client = NewAdminClient(reviewerId, VerificationPermissions.Review);
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/verifications/{verificationId}/approve")
        {
            Content = JsonContent.Create(new
            {
                reason = new { en = "Verified.", ar = "تم التحقق." },
            }),
        };

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be("verification.idempotency.key_missing");
    }
}
