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
/// Spec 020 T104 — HTTP contract suite for
/// <c>POST /api/admin/verifications/{id}/revoke</c> per
/// <see href="../../../../specs/phase-1D/020-verification/contracts/verification-contract.md">contracts §3.6</see>.
///
/// <para><b>Distinct permission:</b> revoke requires
/// <c>verification.revoke</c>; the broader <c>verification.review</c> is
/// NOT enough. A reviewer with only <c>review</c> hits 403 with reason code
/// <c>verification.revoke_permission_required</c>.</para>
///
/// <para><b>State guard:</b> only an <c>approved</c> verification is
/// revocable. Any other state (including <c>submitted</c>, <c>in_review</c>,
/// <c>info_requested</c>, <c>rejected</c>, <c>expired</c>, <c>revoked</c>,
/// <c>superseded</c>) returns <c>verification.invalid_state_for_action</c>
/// with a 409.</para>
/// </summary>
[Collection("VerificationContractCollection")]
public sealed class AdminRevokeContractTests
{
    private readonly VerificationApiFactory _factory;

    public AdminRevokeContractTests(VerificationApiFactory factory) => _factory = factory;

    private HttpClient NewAdminClient(Guid reviewerId, string permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Admin-Id", reviewerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        client.DefaultRequestHeaders.Add("X-Test-Market", "ksa");
        return client;
    }

    private HttpClient NewCustomerClient(Guid customerId, string market = "ksa")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Customer-Id", customerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", market);
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

    /// <summary>
    /// Direct DB stub — promotes a freshly-submitted verification to
    /// <c>approved</c> with an <c>expires_at</c> set so the revoke handler's
    /// state guard passes. Bypasses the reviewer slice's full Decide pipeline
    /// to keep the revoke-side contract assertions focused.
    /// </summary>
    private async Task ForceApprovedStateAsync(Guid verificationId, Guid reviewerId)
    {
        await using var ctx = _factory.NewDbContext();
        var v = await ctx.Verifications.SingleAsync(x => x.Id == verificationId);
        var prior = v.State.ToWireValue();
        v.State = VerificationState.Approved;
        v.DecidedAt = DateTimeOffset.UtcNow;
        v.DecidedBy = reviewerId;
        v.ExpiresAt = DateTimeOffset.UtcNow.AddYears(1);
        v.UpdatedAt = DateTimeOffset.UtcNow;
        ctx.StateTransitions.Add(new VerificationStateTransition
        {
            Id = Guid.NewGuid(),
            VerificationId = verificationId,
            PriorState = prior,
            NewState = VerificationState.Approved.ToWireValue(),
            ActorKind = "reviewer",
            ActorId = reviewerId,
            OccurredAt = DateTimeOffset.UtcNow,
            Reason = "forced_approved_for_test",
            MarketCode = v.MarketCode,
            MetadataJson = "{}",
        });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Revoke_403_when_reviewer_lacks_revoke_permission()
    {
        // Reviewer holds verification.review but NOT verification.revoke.
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var verificationId = await SubmitNewVerificationAsync(customerId);
        await ForceApprovedStateAsync(verificationId, reviewerId);

        using var client = NewAdminClient(reviewerId, VerificationPermissions.Review);
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/verifications/{verificationId}/revoke")
        {
            Content = JsonContent.Create(new
            {
                reason = new { en = "License revoked.", ar = "تم إلغاء الترخيص." },
            }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be("verification.revoke_permission_required");
    }

    [Fact]
    public async Task Revoke_400_reason_required_when_both_locales_blank()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var verificationId = await SubmitNewVerificationAsync(customerId);
        await ForceApprovedStateAsync(verificationId, reviewerId);

        using var client = NewAdminClient(reviewerId, VerificationPermissions.Revoke);
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/verifications/{verificationId}/revoke")
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
    public async Task Revoke_409_invalid_state_when_target_is_submitted_not_approved()
    {
        // Per §3.6: only `approved` is revocable.
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var verificationId = await SubmitNewVerificationAsync(customerId);
        // Verification stays in `submitted`.

        using var client = NewAdminClient(reviewerId, VerificationPermissions.Revoke);
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/verifications/{verificationId}/revoke")
        {
            Content = JsonContent.Create(new
            {
                reason = new { en = "Premature revoke.", ar = "إلغاء سابق لأوانه." },
            }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be("verification.invalid_state_for_action");
    }

    [Fact]
    public async Task Revoke_400_idempotency_key_missing_when_header_absent()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var verificationId = await SubmitNewVerificationAsync(customerId);
        await ForceApprovedStateAsync(verificationId, reviewerId);

        using var client = NewAdminClient(reviewerId, VerificationPermissions.Revoke);
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/verifications/{verificationId}/revoke")
        {
            Content = JsonContent.Create(new
            {
                reason = new { en = "License revoked.", ar = "تم إلغاء الترخيص." },
            }),
        };

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be("verification.idempotency.key_missing");
    }

    [Fact]
    public async Task Revoke_409_already_decided_or_invalid_state_when_concurrent_revoke_loses_race()
    {
        // FR-026 / R4 — optimistic-concurrency contract. The xmin token on
        // verifications guards the Approved → Revoked transition. When two
        // revokes race for the same approval, one wins (200 OK) and the
        // other lands on a 409 with reasonCode in:
        //   • verification.already_decided — loser made it past the state
        //     guard but EF detected an xmin mismatch at SaveChanges;
        //   • verification.invalid_state_for_action — loser loaded the row
        //     after the winner moved it to Revoked, so the state guard
        //     short-circuited before SaveChanges.
        // Both shapes satisfy the wire contract: a concurrent revoke must
        // never produce a duplicate Approved → Revoked transition or a
        // second 200.
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var verificationId = await SubmitNewVerificationAsync(customerId);
        await ForceApprovedStateAsync(verificationId, reviewerId);

        async Task<HttpResponseMessage> SendRevokeAsync()
        {
            // Each request gets its own client + Idempotency-Key so neither
            // is short-circuited by the per-key duplicate-suppression path.
            var client = NewAdminClient(reviewerId, VerificationPermissions.Revoke);
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/verifications/{verificationId}/revoke")
            {
                Content = JsonContent.Create(new
                {
                    reason = new
                    {
                        en = "License revoked.",
                        ar = "تم إلغاء الترخيص.",
                    },
                }),
            };
            req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            return await client.SendAsync(req);
        }

        var responses = await Task.WhenAll(SendRevokeAsync(), SendRevokeAsync());

        var successes = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflicts = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        successes.Should().Be(1, "exactly one revoke must win the race");
        conflicts.Should().Be(1, "the other revoke must lose with a 409");

        var loser = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        var loserBody = await loser.Content.ReadFromJsonAsync<JsonElement>();
        var loserCode = loserBody.GetProperty("reasonCode").GetString();
        loserCode.Should().BeOneOf(
            "verification.already_decided",
            "verification.invalid_state_for_action");

        // Persistence side-effect: row state lands on Revoked exactly once,
        // not double-transitioned.
        await using var verify = _factory.NewDbContext();
        var row = await verify.Verifications.SingleAsync(v => v.Id == verificationId);
        row.State.Should().Be(VerificationState.Revoked);
        var revokeTransitions = await verify.StateTransitions
            .CountAsync(t => t.VerificationId == verificationId
                          && t.NewState == VerificationState.Revoked.ToWireValue());
        revokeTransitions.Should().Be(1, "the losing revoke must not write a duplicate transition row");
    }

    [Fact]
    public async Task Revoke_200_OK_when_caller_has_revoke_permission_and_target_is_approved()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var verificationId = await SubmitNewVerificationAsync(customerId);
        await ForceApprovedStateAsync(verificationId, reviewerId);

        using var client = NewAdminClient(reviewerId, VerificationPermissions.Revoke);
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/verifications/{verificationId}/revoke")
        {
            Content = JsonContent.Create(new
            {
                reason = new
                {
                    en = "License revoked by SCFHS notice 2026-04-25.",
                    ar = "تم إلغاء الترخيص بإشعار الهيئة 2026-04-25.",
                },
            }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("state").GetString().Should().Be("revoked");
        body.GetProperty("id").GetString().Should().Be(verificationId.ToString());

        // Persistence side-effect: row state moves to revoked.
        await using var verify = _factory.NewDbContext();
        var row = await verify.Verifications.SingleAsync(v => v.Id == verificationId);
        row.State.Should().Be(VerificationState.Revoked);
    }
}
