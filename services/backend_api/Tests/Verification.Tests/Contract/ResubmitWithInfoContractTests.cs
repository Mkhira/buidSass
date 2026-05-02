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
/// Spec 020 T046 — HTTP contract suite for
/// <c>POST /api/customer/verifications/{id}/resubmit</c> per
/// <see href="../../../../specs/phase-1D/020-verification/contracts/verification-contract.md">contracts §2.6</see>.
///
/// <para><b>Implementation drift note:</b> the contracts file lists
/// <c>verification.no_changes_provided</c> as a 400 error. PR #46 finalized
/// the implementation so resubmit requires:</para>
/// <list type="bullet">
///   <item>a non-blank <c>acknowledgement</c> string in the body — blank
///         emits <c>verification.required_field_missing</c>;</item>
///   <item>at least one document attached AFTER the latest info-requested
///         transition — emits <c>verification.required_field_missing</c>
///         (with detail prefix <c>no_changes_provided</c>) when none. Both
///         "no acknowledgement" and "no new docs" surface as the same wire
///         code; the differentiation is in the Problem Details
///         <c>detail</c> field. The contract's <c>no_changes_provided</c>
///         name is covered by this single wire code.</item>
/// </list>
/// <para>Asserts state moves to <c>in_review</c> (NOT <c>submitted</c>),
/// preserves <c>submitted_at</c>, and resets <c>decided_at</c>/<c>decided_by</c>.</para>
/// </summary>
[Collection("VerificationContractCollection")]
public sealed class ResubmitWithInfoContractTests
{
    private readonly VerificationApiFactory _factory;

    public ResubmitWithInfoContractTests(VerificationApiFactory factory) => _factory = factory;

    private HttpClient NewCustomerClient(Guid customerId, string market = "ksa")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Customer-Id", customerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", market);
        return client;
    }

    private async Task<Guid> SubmitNewVerificationAsync(HttpClient client)
    {
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
    /// Direct DB stub to set the verification's state to info-requested with
    /// a corresponding state transition row. Bypasses the reviewer slice so
    /// the resubmit-side contract assertions stay focused.
    /// </summary>
    private async Task ForceInfoRequestedStateAsync(Guid verificationId)
    {
        await using var ctx = _factory.NewDbContext();
        var v = await ctx.Verifications.SingleAsync(x => x.Id == verificationId);
        var prior = v.State.ToWireValue();
        v.State = VerificationState.InfoRequested;
        v.UpdatedAt = DateTimeOffset.UtcNow;
        ctx.StateTransitions.Add(new VerificationStateTransition
        {
            Id = Guid.NewGuid(),
            VerificationId = verificationId,
            PriorState = prior,
            NewState = VerificationState.InfoRequested.ToWireValue(),
            ActorKind = "reviewer",
            ActorId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Reason = "forced_info_requested_for_test",
            MarketCode = v.MarketCode,
            MetadataJson = "{}",
        });
        await ctx.SaveChangesAsync();
    }

    private async Task AttachOneCleanDocumentAsync(HttpClient client, Guid verificationId)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/verifications/{verificationId}/documents")
        {
            Content = JsonContent.Create(new
            {
                storageKey = $"spec-015/license-{Guid.NewGuid():N}.png",
                contentType = "image/png",
                sizeBytes = 500_000L,
                scanStatus = "clean",
            }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Resubmit_200_state_moves_to_in_review_and_preserves_submitted_at()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        using var client = NewCustomerClient(customerId);

        var verificationId = await SubmitNewVerificationAsync(client);
        DateTimeOffset originalSubmittedAt;
        await using (var ctx = _factory.NewDbContext())
        {
            originalSubmittedAt = (await ctx.Verifications.SingleAsync(v => v.Id == verificationId))
                .SubmittedAt;
        }

        await ForceInfoRequestedStateAsync(verificationId);
        await AttachOneCleanDocumentAsync(client, verificationId);

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/verifications/{verificationId}/resubmit")
        {
            Content = JsonContent.Create(new { acknowledgement = "I uploaded a clearer license image." }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("state").GetString().Should().Be("in-review",
            "resubmit moves the row to in-review per FR-016, NOT back to submitted");
        body.GetProperty("submittedAt").GetDateTimeOffset().Should().Be(originalSubmittedAt,
            "the original submitted_at is preserved across resubmit");

        // decided_at + decided_by reset to null until next decision.
        await using var verify = _factory.NewDbContext();
        var row = await verify.Verifications.SingleAsync(v => v.Id == verificationId);
        row.DecidedAt.Should().BeNull();
        row.DecidedBy.Should().BeNull();
    }

    [Fact]
    public async Task Resubmit_400_required_field_missing_when_acknowledgement_blank()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        using var client = NewCustomerClient(customerId);
        var verificationId = await SubmitNewVerificationAsync(client);
        await ForceInfoRequestedStateAsync(verificationId);
        await AttachOneCleanDocumentAsync(client, verificationId);

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/verifications/{verificationId}/resubmit")
        {
            Content = JsonContent.Create(new { acknowledgement = "   " }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertProblemAsync(resp, "verification.required_field_missing");
    }

    [Fact]
    public async Task Resubmit_409_invalid_state_when_not_info_requested()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        using var client = NewCustomerClient(customerId);
        var verificationId = await SubmitNewVerificationAsync(client);
        // Verification is in `submitted`, NOT info_requested.

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/verifications/{verificationId}/resubmit")
        {
            Content = JsonContent.Create(new { acknowledgement = "any string" }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertProblemAsync(resp, "verification.invalid_state_for_action");
    }

    [Fact]
    public async Task Resubmit_400_required_field_missing_when_no_new_documents_after_info_requested()
    {
        // FR-016: resubmit must include at least one new document attached
        // AFTER the latest info_requested transition. Skipping the attach
        // step puts the verification into a "no changes provided" state —
        // the handler emits required_field_missing with detail prefix
        // "no_changes_provided" (the contract's no_changes_provided wire
        // code is collapsed into required_field_missing per the drift note).
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        using var client = NewCustomerClient(customerId);
        var verificationId = await SubmitNewVerificationAsync(client);
        await ForceInfoRequestedStateAsync(verificationId);
        // Intentionally NOT calling AttachOneCleanDocumentAsync — the row is
        // in info_requested with the original submission's docs, none after.

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/verifications/{verificationId}/resubmit")
        {
            Content = JsonContent.Create(new { acknowledgement = "I have re-read the prior request." }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be("verification.required_field_missing");
        body.GetProperty("detail").GetString().Should().Contain("no_changes_provided",
            "the detail field tags this as the no_changes_provided variant of required_field_missing");
    }

    [Fact]
    public async Task Resubmit_404_when_caller_is_not_owner()
    {
        await _factory.ResetVerificationAsync();
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        using var clientA = NewCustomerClient(customerA);
        using var clientB = NewCustomerClient(customerB);

        var verificationId = await SubmitNewVerificationAsync(clientA);
        await ForceInfoRequestedStateAsync(verificationId);
        await AttachOneCleanDocumentAsync(clientA, verificationId);

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/verifications/{verificationId}/resubmit")
        {
            Content = JsonContent.Create(new { acknowledgement = "I am customer B." }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await clientB.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertProblemAsync(resp, "verification.not_found");
    }

    private static async Task AssertProblemAsync(HttpResponseMessage resp, string expectedReasonCode)
    {
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be(expectedReasonCode);
    }
}
