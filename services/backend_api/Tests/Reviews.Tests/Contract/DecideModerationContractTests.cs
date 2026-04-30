using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Reviews.Admin.DecideModeration;
using BackendApi.Modules.Reviews.Authorization;
using BackendApi.Modules.Reviews.Customer.SubmitReview;
using BackendApi.Modules.Reviews.Primitives;
using FluentAssertions;
using Reviews.Tests.Contract.Infrastructure;

namespace Reviews.Tests.Contract;

/// <summary>
/// Spec 022 T084 — HTTP contract for POST /api/admin/reviews/{id}/decide.
/// Wire-shape only; covers RBAC gates + validation reason codes.
/// </summary>
public sealed class DecideModerationContractTests : IClassFixture<ReviewsApiFactory>
{
    private readonly ReviewsApiFactory _factory;

    public DecideModerationContractTests(ReviewsApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Caller_without_moderator_permission_returns_403()
    {
        var (_, reviewId) = await SubmitVisibleReviewAsync();
        var admin = _factory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Test-Admin-Id", Guid.NewGuid().ToString());
        // No permissions claim — moderator gate must fail.

        var resp = await admin.PostAsJsonAsync($"/api/admin/reviews/{reviewId}/decide",
            new DecideModerationRequest("hidden", "Sufficiently long reason note.", null));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be(ReviewReasonCode.ModerationForbidden);
    }

    [Fact]
    public async Task Hide_without_reason_note_returns_400_with_reason_required()
    {
        var (_, reviewId) = await SubmitVisibleReviewAsync();
        var admin = ModeratorClient();

        var resp = await admin.PostAsJsonAsync($"/api/admin/reviews/{reviewId}/decide",
            new DecideModerationRequest("hidden", "short", null));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be(ReviewReasonCode.ModerationReasonRequired);
    }

    [Fact]
    public async Task Delete_without_super_admin_returns_403_with_delete_requires_super_admin()
    {
        var (_, reviewId) = await SubmitVisibleReviewAsync();
        var admin = ModeratorClient();

        var resp = await admin.PostAsJsonAsync($"/api/admin/reviews/{reviewId}/decide",
            new DecideModerationRequest("deleted", "Sufficiently long reason note.", null));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be(ReviewReasonCode.ModerationDeleteRequiresSuperAdmin);
    }

    [Fact]
    public async Task Hard_delete_method_returns_405_with_delete_forbidden()
    {
        var admin = ModeratorClient();
        var resp = await admin.DeleteAsync($"/api/admin/reviews/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be(ReviewReasonCode.RowDeleteForbidden);
    }

    private HttpClient ModeratorClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Admin-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", ReviewsPermissions.Moderator);
        return client;
    }

    private async Task<(HttpClient client, Guid reviewId)> SubmitVisibleReviewAsync()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Customer-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", "SA");
        var resp = await client.PostAsJsonAsync("/api/customer/reviews",
            new SubmitReviewRequest(Guid.NewGuid(), 5, "Headline",
                "Clean body content here long enough.", "en", null));
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return (client, body.GetProperty("id").GetGuid());
    }
}
