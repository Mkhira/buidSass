using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Verification.Authorization;
using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Primitives;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Verification.Tests.Contract.Infrastructure;

namespace Verification.Tests.Contract;

/// <summary>
/// Spec 020 T068 — HTTP contract suite for
/// <c>GET /api/admin/verifications/{id}/documents/{documentId}/open</c> per
/// <see href="../../../../specs/phase-1D/020-verification/contracts/verification-contract.md">contracts §3.7</see>.
///
/// <para>Asserts the three branches of the document-open contract:</para>
/// <list type="bullet">
///   <item>Non-terminal parent → 200 OK with signed URL.</item>
///   <item>Purged document → 410 with reason code
///         <c>verification.document_purged</c>.</item>
///   <item>Terminal parent → 200 OK with signed URL and the
///         <c>isHistoricalOpen</c> flag set to true (the second
///         <c>verification.pii_access</c> audit row is asserted in the
///         AdminRevokeAndOpenHistoricalDoc integration tests).</item>
/// </list>
/// </summary>
[Collection("VerificationContractCollection")]
public sealed class OpenHistoricalDocumentContractTests
{
    private readonly VerificationApiFactory _factory;

    public OpenHistoricalDocumentContractTests(VerificationApiFactory factory) => _factory = factory;

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

    /// <summary>
    /// Direct DB insert — drops a document row so the open endpoint has
    /// something to find. We bypass the AttachDocument slice because the
    /// upstream-scan contract requires storage-side coordination outside
    /// these tests' scope; the open endpoint's contract only depends on the
    /// row existing with the expected fields.
    /// </summary>
    private async Task<Guid> InsertDocumentAsync(
        Guid verificationId,
        string? storageKey = "verifications/contract-test/doc.pdf",
        DateTimeOffset? purgedAt = null)
    {
        await using var db = _factory.NewDbContext();
        var docId = Guid.NewGuid();
        db.Documents.Add(new VerificationDocument
        {
            Id = docId,
            VerificationId = verificationId,
            MarketCode = "ksa",
            StorageKey = storageKey,
            ContentType = "application/pdf",
            SizeBytes = 4096,
            ScanStatus = "clean",
            UploadedAt = DateTimeOffset.UtcNow,
            PurgedAt = purgedAt,
        });
        await db.SaveChangesAsync();
        return docId;
    }

    [Fact]
    public async Task Open_200_OK_with_signed_url_for_non_terminal_parent()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitNewVerificationAsync(customerId);

        // Insert a StoredFile row so LocalDiskStorageService.GetSignedUrlAsync
        // can resolve the document body. The verification document's
        // StorageKey must be the StoredFile.Id (Guid) per
        // LocalDiskStorageService.GetSignedUrlAsync's contract.
        var storedFileId = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var appDb = scope.ServiceProvider.GetRequiredService<BackendApi.Modules.Shared.AppDbContext>();
            appDb.StoredFiles.Add(new BackendApi.Modules.Storage.StoredFile
            {
                Id = storedFileId,
                BucketKey = $"ksa/{storedFileId}-doc.pdf",
                Market = "ksa",
                OriginalFilename = "doc.pdf",
                SizeBytes = 4096,
                MimeType = "application/pdf",
                VirusScanStatus = "clean",
                UploadedAt = DateTimeOffset.UtcNow,
            });
            await appDb.SaveChangesAsync();
        }

        var documentId = await InsertDocumentAsync(verificationId, storageKey: storedFileId.ToString());

        var reviewerId = Guid.NewGuid();
        using var client = NewAdminClient(reviewerId, VerificationPermissions.Review);

        var resp = await client.GetAsync(
            $"/api/admin/verifications/{verificationId}/documents/{documentId}/open");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("verificationId").GetString().Should().Be(verificationId.ToString());
        body.GetProperty("documentId").GetString().Should().Be(documentId.ToString());
        body.GetProperty("signedUrl").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("isHistoricalOpen").GetBoolean()
            .Should().BeFalse("submitted is non-terminal — historical_open=false");
    }

    [Fact]
    public async Task Open_200_OK_with_isHistoricalOpen_true_for_terminal_parent()
    {
        // T068 / contracts §3.7 — the terminal-parent branch documented at
        // class-summary lines 24–27. When the parent verification is in any
        // terminal state (rejected / expired / revoked / superseded / void)
        // the endpoint returns 200 with `isHistoricalOpen=true`. We use
        // `revoked` here (the most operationally relevant terminal — admin
        // pulls a revoked customer's docs for an audit), but the branch is
        // covered for any terminal state. Direct DB flip — bypassing the
        // decide endpoints keeps this contract test focused on the
        // wire-level behavior of the open endpoint.
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitNewVerificationAsync(customerId);

        var storedFileId = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var appDb = scope.ServiceProvider.GetRequiredService<BackendApi.Modules.Shared.AppDbContext>();
            appDb.StoredFiles.Add(new BackendApi.Modules.Storage.StoredFile
            {
                Id = storedFileId,
                BucketKey = $"ksa/{storedFileId}-doc.pdf",
                Market = "ksa",
                OriginalFilename = "doc.pdf",
                SizeBytes = 4096,
                MimeType = "application/pdf",
                VirusScanStatus = "clean",
                UploadedAt = DateTimeOffset.UtcNow,
            });
            await appDb.SaveChangesAsync();
        }

        var documentId = await InsertDocumentAsync(verificationId, storageKey: storedFileId.ToString());

        // Flip parent → revoked terminal state directly (the open endpoint
        // only inspects parent.State, not the transition ledger).
        await using (var db = _factory.NewDbContext())
        {
            var v = await db.Verifications.SingleAsync(x => x.Id == verificationId);
            v.State = VerificationState.Revoked;
            v.DecidedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        var reviewerId = Guid.NewGuid();
        using var client = NewAdminClient(reviewerId, VerificationPermissions.Review);

        var resp = await client.GetAsync(
            $"/api/admin/verifications/{verificationId}/documents/{documentId}/open");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("verificationId").GetString().Should().Be(verificationId.ToString());
        body.GetProperty("documentId").GetString().Should().Be(documentId.ToString());
        body.GetProperty("signedUrl").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("isHistoricalOpen").GetBoolean()
            .Should().BeTrue("revoked parent is terminal — historical_open=true (FR-022a)");
    }

    [Fact]
    public async Task Open_410_purged_when_document_body_already_purged()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitNewVerificationAsync(customerId);
        var purgedAt = DateTimeOffset.UtcNow.AddDays(-30);
        var documentId = await InsertDocumentAsync(
            verificationId,
            storageKey: null,
            purgedAt: purgedAt);

        var reviewerId = Guid.NewGuid();
        using var client = NewAdminClient(reviewerId, VerificationPermissions.Review);

        var resp = await client.GetAsync(
            $"/api/admin/verifications/{verificationId}/documents/{documentId}/open");

        resp.StatusCode.Should().Be(HttpStatusCode.Gone);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be("verification.document_purged");
    }

    [Fact]
    public async Task Open_404_not_found_when_document_does_not_exist()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitNewVerificationAsync(customerId);
        var unknownDocId = Guid.NewGuid();

        var reviewerId = Guid.NewGuid();
        using var client = NewAdminClient(reviewerId, VerificationPermissions.Review);

        var resp = await client.GetAsync(
            $"/api/admin/verifications/{verificationId}/documents/{unknownDocId}/open");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be("verification.document_not_found");
    }

    [Fact]
    public async Task Open_403_when_reviewer_lacks_review_permission()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitNewVerificationAsync(customerId);
        var documentId = await InsertDocumentAsync(verificationId);

        var reviewerId = Guid.NewGuid();
        // Reviewer holds revoke but not review — open requires review.
        using var client = NewAdminClient(reviewerId, VerificationPermissions.Revoke);

        var resp = await client.GetAsync(
            $"/api/admin/verifications/{verificationId}/documents/{documentId}/open");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be("verification.review_permission_required");
    }
}
