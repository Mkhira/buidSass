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
/// Spec 020 T066 — HTTP contract suite for
/// <c>POST /api/admin/verifications/{id}/reject</c> per
/// <see href="../../../../specs/phase-1D/020-verification/contracts/verification-contract.md">contracts §3.4</see>.
///
/// <para>Rejection has the same request shape as approval but transitions to
/// <c>rejected</c> instead. Asserts: 200 OK happy path; 400
/// <c>verification.review.reason_required</c> on empty bilingual reason;
/// row state moves to rejected.</para>
/// </summary>
[Collection("VerificationContractCollection")]
public sealed class AdminRejectContractTests
{
    private readonly VerificationApiFactory _factory;

    public AdminRejectContractTests(VerificationApiFactory factory) => _factory = factory;

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
    public async Task Reject_200_OK_on_happy_path_and_state_moves_to_rejected()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitNewVerificationAsync(customerId);

        var reviewerId = Guid.NewGuid();
        using var client = NewAdminClient(reviewerId, VerificationPermissions.Review);
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/verifications/{verificationId}/reject")
        {
            Content = JsonContent.Create(new
            {
                reason = new
                {
                    en = "Document quality insufficient — please resubmit a clearer scan.",
                    ar = "جودة المستند غير كافية — يرجى إعادة إرسال نسخة أوضح.",
                },
            }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("state").GetString().Should().Be("rejected");

        await using var db = _factory.NewDbContext();
        var row = await db.Verifications.AsNoTracking()
            .SingleAsync(v => v.Id == verificationId);
        row.State.Should().Be(VerificationState.Rejected);
        row.DecidedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Reject_400_reason_required_when_both_locales_blank()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitNewVerificationAsync(customerId);

        var reviewerId = Guid.NewGuid();
        using var client = NewAdminClient(reviewerId, VerificationPermissions.Review);
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/verifications/{verificationId}/reject")
        {
            Content = JsonContent.Create(new
            {
                reason = new { en = "", ar = "   " },
            }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be("verification.review.reason_required");
    }
}
