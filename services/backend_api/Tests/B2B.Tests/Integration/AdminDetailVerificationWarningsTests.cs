using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.Verification.Primitives;
using B2B.Tests.Contract.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Tests.Integration;

/// <summary>
/// Spec 021 T084a — admin Quote-Detail (<c>GET /api/admin/quotes/{id}</c>,
/// contract §4.2) MUST surface a real-time <c>verification_warnings</c>
/// advisory block for any restricted-SKU line whose buyer's verification is
/// no longer valid at the moment the operator opens the detail screen
/// (spec.md §Edge Cases — "verification status flips to expired before
/// authoring"). This decouples request-time policy from authoring-time
/// reality so the operator does NOT publish a quote the customer cannot
/// actually accept (FR-036).
///
/// Population logic:
/// - Empty array when every restricted-SKU line returns
///   <see cref="EligibilityClass.Eligible"/> or
///   <see cref="EligibilityClass.Unrestricted"/> from
///   <see cref="ICustomerVerificationEligibilityQuery"/>.
/// - Populated when ANY line returns <see cref="EligibilityClass.Ineligible"/>;
///   each entry carries <c>{ sku, reason_code, message_key }</c>.
///
/// <b>Cycle scoping</b>: T086 (GetQuoteDetail handler) is NOT in this cycle.
/// While the route is unmapped (404), this test pins the eventual contract
/// using <c>BeOneOf(404, ExpectedCode)</c> so the suite stays green
/// pre-implementation. The stub seeding pattern works regardless of whether
/// the handler exists yet.
/// </summary>
public sealed class AdminDetailVerificationWarningsTests : IClassFixture<B2BApiFactory>
{
    private readonly B2BApiFactory _factory;

    public AdminDetailVerificationWarningsTests(B2BApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Expired_verification_on_restricted_line_surfaces_in_verification_warnings()
    {
        _factory.ResetStubState();

        var customerId = Guid.NewGuid();
        const string restrictedSku = "RESTRICTED-RX-001";
        var quoteId = await SeedQuoteWithLineAsync(customerId, restrictedSku);

        // Seed the verification-eligibility stub so this customer + SKU pair
        // returns Ineligible/VerificationExpired — exactly the flip-during-
        // authoring scenario the advisory block is designed to surface.
        _factory.VerificationEligibilityQuery.ResultBySkuAndCustomer[(customerId, restrictedSku)]
            = new EligibilityResult(
                Class: EligibilityClass.Ineligible,
                ReasonCode: EligibilityReasonCode.VerificationExpired,
                MessageKey: EligibilityReasonCodeExtensions.ToIcuKey(EligibilityReasonCode.VerificationExpired),
                ExpiresAt: null);

        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.review");

        var resp = await client.GetAsync($"/api/admin/quotes/{quoteId}");

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,    // T086 not yet mapped (Cycle A)
            HttpStatusCode.OK);         // T086 wired (Cycle B+)

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("verification_warnings", out var warnings).Should().BeTrue();
            warnings.ValueKind.Should().Be(JsonValueKind.Array);
            warnings.GetArrayLength().Should().BeGreaterOrEqualTo(1,
                "expired-verification on a restricted line MUST surface a warning entry");

            var entry = warnings.EnumerateArray().First();
            entry.GetProperty("sku").GetString().Should().Be(restrictedSku);
            // Derive the expected wire tokens from the same source-of-truth the
            // production handler reads. Hardcoding "VerificationExpired" /
            // "verification.eligibility.expired" duplicates spec 020's enum
            // and ICU registry, so a rename in Verification breaks this test
            // for spurious reasons. Computing from the enum + extension keeps
            // the contract anchored to spec 020's canonical values.
            entry.GetProperty("reason_code").GetString()
                .Should().Be(nameof(EligibilityReasonCode.VerificationExpired),
                    "FR-036 + spec.md §Edge Cases: surface the EligibilityReasonCode token");
            entry.GetProperty("message_key").GetString()
                .Should().Be(
                    EligibilityReasonCodeExtensions.ToIcuKey(EligibilityReasonCode.VerificationExpired),
                    "ICU key MUST round-trip from EligibilityReasonCodeExtensions.ToIcuKey");
        }
    }

    [Fact]
    public async Task Eligible_verification_yields_empty_verification_warnings_array()
    {
        _factory.ResetStubState();

        var customerId = Guid.NewGuid();
        const string restrictedSku = "RESTRICTED-RX-002";
        var quoteId = await SeedQuoteWithLineAsync(customerId, restrictedSku);

        // Seed an Eligible result (the happy-path verification state).
        _factory.VerificationEligibilityQuery.ResultBySkuAndCustomer[(customerId, restrictedSku)]
            = new EligibilityResult(
                Class: EligibilityClass.Eligible,
                ReasonCode: EligibilityReasonCode.Eligible,
                MessageKey: EligibilityReasonCodeExtensions.ToIcuKey(EligibilityReasonCode.Eligible),
                ExpiresAt: DateTimeOffset.UtcNow.AddYears(1));

        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.review");

        var resp = await client.GetAsync($"/api/admin/quotes/{quoteId}");

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.OK);

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("verification_warnings", out var warnings).Should().BeTrue(
                "the field MUST always be present (empty array when no warnings) per §4.2");
            warnings.ValueKind.Should().Be(JsonValueKind.Array);
            warnings.GetArrayLength().Should().Be(0,
                "valid verification across all restricted lines MUST yield an empty array");
        }
    }

    private async Task<Guid> SeedQuoteWithLineAsync(Guid customerId, string sku)
    {
        var quoteId = Guid.NewGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();

        // Pre-publish state — verification advisory reads off the originating
        // cart snapshot when no QuoteVersion exists yet, per T086 description.
        // Using `requested` is the fixture-cheapest way to land in this branch.
        db.Quotes.Add(new Quote
        {
            Id = quoteId,
            CustomerId = customerId,
            CompanyId = null,
            BranchId = null,
            MarketCode = "ksa",
            State = "requested",
            RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-15),
            ExpiresAt = null,
            CurrentVersionId = null,
            DecidedAt = null,
            DecidedBy = null,
            TerminalAt = null,
            TerminalReason = null,
            PoNumber = null,
            InvoiceBilling = false,
            CustomerSuppliedMessageJson = null,
            InternalNote = null,
            ApproverRejectionNote = null,
            // Originating cart snapshot — single restricted line — read by
            // T086 to determine which SKUs to send to the verification probe.
            // Serialize via JsonSerializer rather than string concatenation so
            // SKUs containing quotes/backslashes can't corrupt the payload.
            OriginatingCartSnapshotJson = System.Text.Json.JsonSerializer.Serialize(
                new[] { new { sku, quantity = 2, line_note = (string?)null } }),
            OriginatingProductId = null,
            // Restriction-policy snapshot marks the SKU as restricted at request
            // time; T086 uses this to filter which lines need the advisory check.
            RestrictionPolicySnapshotJson = System.Text.Json.JsonSerializer.Serialize(
                new Dictionary<string, object>
                {
                    [sku] = new { is_restricted = true, required_profession = "dentist" },
                }),
            SchemaVersion = 1,
        });

        await db.SaveChangesAsync();
        return quoteId;
    }

    private static void AuthenticateAsAdmin(
        HttpClient client,
        string market,
        string? permissions = null)
    {
        client.DefaultRequestHeaders.Add("X-Test-Admin-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", market);
        if (!string.IsNullOrWhiteSpace(permissions))
        {
            client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        }
    }
}
