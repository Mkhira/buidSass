using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Reviews.Authorization;
using BackendApi.Modules.Reviews.PolicyAdmin.UpdateMarketSchema;
using BackendApi.Modules.Reviews.Primitives;
using FluentAssertions;
using Reviews.Tests.Contract.Infrastructure;

namespace Reviews.Tests.Contract;

/// <summary>
/// Spec 022 T131 — HTTP contract for PATCH /api/admin/reviews/policy/markets/{market}
/// — non-policy_admin caller must be rejected with the canonical reason code.
/// </summary>
public sealed class UpdateMarketSchemaContractTests : IClassFixture<ReviewsApiFactory>
{
    private readonly ReviewsApiFactory _factory;

    public UpdateMarketSchemaContractTests(ReviewsApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Non_policy_admin_returns_403_with_policy_forbidden()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Admin-Id", Guid.NewGuid().ToString());
        // Caller has reviews.moderator but NOT reviews.policy_admin — gate must fail.
        client.DefaultRequestHeaders.Add("X-Test-Permissions", ReviewsPermissions.Moderator);

        var resp = await client.PatchAsJsonAsync("/api/admin/reviews/policy/markets/SA",
            new UpdateMarketSchemaRequest(EligibilityWindowDays: 200, null, null, null, null, null, null));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be(ReviewReasonCode.PolicyForbidden);
    }
}
