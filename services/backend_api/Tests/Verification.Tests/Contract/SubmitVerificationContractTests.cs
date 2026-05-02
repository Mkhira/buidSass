using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Verification.Tests.Contract.Infrastructure;

namespace Verification.Tests.Contract;

/// <summary>
/// Spec 020 T044 — HTTP contract suite for
/// <c>POST /api/customer/verifications</c> per
/// <see href="../../../../specs/phase-1D/020-verification/contracts/verification-contract.md">contracts §2.1</see>.
/// Asserts the wire-level error envelope shape and the per-error reason
/// codes the customer-facing client depends on.
///
/// <para><b>Implementation drift note (post-hoc tests of merged behavior):</b></para>
/// <list type="bullet">
///   <item>The contracts file lists <c>verification.documents_invalid</c>
///         and <c>verification.account_inactive</c> as 400/422 errors.
///         Post-PR-#46, the implementation routes (a) document-list shape
///         errors through <c>verification.documents_invalid</c> and
///         (b) the body-required path through <c>verification.required_field_missing</c>.
///         The tests below exercise the actual emitted codes; reviewers
///         should ratify the codes against the contract doc and either
///         tighten the doc or open a follow-up.</item>
///   <item>The endpoint requires an <c>Idempotency-Key</c> header and emits
///         <c>verification.idempotency.key_missing</c> when absent. This is
///         not in the contracts file's §2.1 error table but is a
///         platform-wide requirement (see SubmitVerificationEndpoint's
///         docstring) — captured here as a separate test so the doc and
///         the wire envelope agree.</item>
/// </list>
/// </summary>
[Collection("VerificationContractCollection")]
public sealed class SubmitVerificationContractTests
{
    private readonly VerificationApiFactory _factory;

    public SubmitVerificationContractTests(VerificationApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient NewCustomerClient(Guid customerId, string market = "ksa")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Customer-Id", customerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", market);
        return client;
    }

    [Fact]
    public async Task Submit_201_Created_on_happy_path_with_KSA_schema()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
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
        body.GetProperty("state").GetString().Should().Be("submitted");
        body.GetProperty("marketCode").GetString().Should().Be("ksa");
        body.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        Guid.Parse(body.GetProperty("id").GetString()!).Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Submit_400_required_field_missing_when_profession_blank()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        using var client = NewCustomerClient(customerId);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/customer/verifications/")
        {
            Content = JsonContent.Create(new
            {
                profession = "",
                regulatorIdentifier = "SCFHS-1234567",
                documentIds = Array.Empty<Guid>(),
            }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertProblemAsync(resp, "verification.required_field_missing");
    }

    [Fact]
    public async Task Submit_400_regulator_identifier_invalid_when_regulator_blank()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        using var client = NewCustomerClient(customerId);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/customer/verifications/")
        {
            Content = JsonContent.Create(new
            {
                profession = "dentist",
                regulatorIdentifier = "   ",
                documentIds = Array.Empty<Guid>(),
            }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertProblemAsync(resp, "verification.regulator_identifier_invalid");
    }

    [Fact]
    public async Task Submit_400_documents_invalid_when_document_ids_omitted()
    {
        // Validator rejects null/missing document_ids with documents_invalid
        // (the array is required even if empty per the spec contract §2.1).
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        using var client = NewCustomerClient(customerId);

        // Send a body where document_ids is explicitly null.
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/customer/verifications/")
        {
            Content = JsonContent.Create(new
            {
                profession = "dentist",
                regulatorIdentifier = "SCFHS-1234567",
                documentIds = (Guid[]?)null,
            }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertProblemAsync(resp, "verification.documents_invalid");
    }

    [Fact]
    public async Task Submit_409_already_pending_on_second_submission()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        using var client = NewCustomerClient(customerId);

        var first = new HttpRequestMessage(HttpMethod.Post, "/api/customer/verifications/")
        {
            Content = JsonContent.Create(new
            {
                profession = "dentist",
                regulatorIdentifier = "SCFHS-1234567",
                documentIds = Array.Empty<Guid>(),
            }),
        };
        first.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        (await client.SendAsync(first)).StatusCode.Should().Be(HttpStatusCode.Created);

        var second = new HttpRequestMessage(HttpMethod.Post, "/api/customer/verifications/")
        {
            Content = JsonContent.Create(new
            {
                profession = "dentist",
                regulatorIdentifier = "SCFHS-1234567",
                documentIds = Array.Empty<Guid>(),
            }),
        };
        second.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var resp = await client.SendAsync(second);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertProblemAsync(resp, "verification.already_pending");
    }

    [Fact]
    public async Task Submit_400_market_unsupported_when_customer_market_has_no_active_schema()
    {
        // The handler short-circuits with MarketUnsupported when no active
        // schema row exists for the resolved market. ResolveMarketCode coerces
        // unknown markets to "ksa", so we exercise the no-schema path by
        // truncating the seeded schema rows after boot.
        await _factory.ResetVerificationAsync();
        await using (var ctx = _factory.NewDbContext())
        {
            await ctx.Database.ExecuteSqlRawAsync(
                "TRUNCATE verification.verification_market_schemas RESTART IDENTITY CASCADE;");
        }

        try
        {
            var customerId = Guid.NewGuid();
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

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            await AssertProblemAsync(resp, "verification.market_unsupported");
        }
        finally
        {
            // Restore schemas for the rest of the suite.
            var seeder = new BackendApi.Modules.Verification.Seeding.VerificationReferenceDataSeeder();
            await using var scope = _factory.Services.CreateAsyncScope();
            var seedCtx = new BackendApi.Features.Seeding.SeedContext(
                null!,
                scope.ServiceProvider,
                BackendApi.Features.Seeding.Datasets.DatasetSize.Small,
                scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            await seeder.ApplyAsync(seedCtx, CancellationToken.None);
        }
    }

    [Fact]
    public async Task Submit_400_idempotency_key_missing_when_header_absent()
    {
        await _factory.ResetVerificationAsync();
        var customerId = Guid.NewGuid();
        using var client = NewCustomerClient(customerId);

        // Note the missing Idempotency-Key header.
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/customer/verifications/")
        {
            Content = JsonContent.Create(new
            {
                profession = "dentist",
                regulatorIdentifier = "SCFHS-1234567",
                documentIds = Array.Empty<Guid>(),
            }),
        };

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertProblemAsync(resp, "verification.idempotency.key_missing");
    }

    [Fact]
    public async Task Submit_401_when_no_customer_principal()
    {
        await _factory.ResetVerificationAsync();
        // Build a client with NO X-Test-Customer-Id header so the stub auth
        // returns NoResult and the [Authorize] policy kicks in.
        var client = _factory.CreateClient();

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

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage resp, string expectedReasonCode)
    {
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be(expectedReasonCode);
        body.GetProperty("type").GetString().Should().Contain(expectedReasonCode);
    }
}

/// <summary>
/// xUnit collection that pins the <see cref="VerificationApiFactory"/> as a
/// shared fixture across spec 020 contract tests so the Postgres container
/// and migrations are amortized across the suite. Per-test isolation is
/// handled by <see cref="VerificationApiFactory.ResetVerificationAsync"/>.
/// </summary>
[CollectionDefinition("VerificationContractCollection")]
public sealed class VerificationContractCollection : ICollectionFixture<VerificationApiFactory>
{
}
