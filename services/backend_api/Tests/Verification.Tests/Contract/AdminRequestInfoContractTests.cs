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
/// Spec 020 T067 — HTTP contract suite for
/// <c>POST /api/admin/verifications/{id}/request-info</c> per
/// <see href="../../../../specs/phase-1D/020-verification/contracts/verification-contract.md">contracts §3.5</see>.
///
/// <para>Asserts: 200 OK transitions state to <c>info-requested</c>; bilingual
/// reason validation; SLA-timer pause is reflected in the row's recorded
/// state (FR-039). Concrete SLA-pause behavior — that age is computed from
/// the most-recent transition out of <c>info-requested</c> — is exercised in
/// the corresponding integration tests; here we anchor the wire contract.</para>
/// </summary>
[Collection("VerificationContractCollection")]
public sealed class AdminRequestInfoContractTests
{
    private readonly VerificationApiFactory _factory;

    public AdminRequestInfoContractTests(VerificationApiFactory factory) => _factory = factory;

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
    public async Task RequestInfo_200_OK_transitions_to_info_requested_and_records_pause()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitNewVerificationAsync(customerId);

        var reviewerId = Guid.NewGuid();
        using var client = NewAdminClient(reviewerId, VerificationPermissions.Review);
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/verifications/{verificationId}/request-info")
        {
            Content = JsonContent.Create(new
            {
                reason = new
                {
                    en = "Please upload a higher-resolution copy of your license.",
                    ar = "يرجى تحميل نسخة عالية الدقة من ترخيصك.",
                },
            }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("state").GetString().Should().Be("info-requested");

        // SLA-timer pause invariant (FR-039): the row records a transition into
        // info-requested, which the queue handler uses to exclude paused rows
        // from age computation. Direct age subtraction is covered in the
        // ListVerificationQueue integration tests; here we assert the row
        // reaches the paused state with a ledger entry.
        await using var db = _factory.NewDbContext();
        var row = await db.Verifications.AsNoTracking()
            .SingleAsync(v => v.Id == verificationId);
        row.State.Should().Be(VerificationState.InfoRequested);

        var infoTransitions = await db.StateTransitions.AsNoTracking()
            .CountAsync(t => t.VerificationId == verificationId
                          && t.NewState == "info-requested");
        infoTransitions.Should().BeGreaterThanOrEqualTo(1,
            "transition into info-requested must be appended to the ledger so the queue handler can pause SLA age");
    }

    [Fact]
    public async Task RequestInfo_400_reason_required_when_both_locales_blank()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitNewVerificationAsync(customerId);

        var reviewerId = Guid.NewGuid();
        using var client = NewAdminClient(reviewerId, VerificationPermissions.Review);
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/verifications/{verificationId}/request-info")
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
}
