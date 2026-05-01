using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Reviews.Customer.ReportReview;
using BackendApi.Modules.Reviews.Customer.SubmitReview;
using BackendApi.Modules.Reviews.Primitives;
using FluentAssertions;
using Reviews.Tests.Contract.Infrastructure;

namespace Reviews.Tests.Contract;

/// <summary>
/// Spec 022 T074 + T075 — HTTP contract for POST /api/customer/reviews/{id}/report.
/// Wire-shape only.
/// </summary>
public sealed class ReportReviewContractTests : IClassFixture<ReviewsApiFactory>
{
    private readonly ReviewsApiFactory _factory;

    public ReportReviewContractTests(ReviewsApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Unauthenticated_request_returns_401()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync($"/api/customer/reviews/{Guid.NewGuid()}/report",
            new ReportReviewRequest("personal_attack", null));
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Self_report_returns_400_with_cannot_report_own_reason()
    {
        var (client, customerId, reviewId) = await SubmitVisibleReviewAsync();

        var resp = await client.PostAsJsonAsync($"/api/customer/reviews/{reviewId}/report",
            new ReportReviewRequest("personal_attack", null));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be(ReviewReasonCode.ReportCannotReportOwnReview);
    }

    [Fact]
    public async Task Invalid_reason_returns_400_with_reason_invalid()
    {
        var (_, _, reviewId) = await SubmitVisibleReviewAsync();
        var reporterClient = _factory.CreateClient();
        reporterClient.DefaultRequestHeaders.Add("X-Test-Customer-Id", Guid.NewGuid().ToString());
        reporterClient.DefaultRequestHeaders.Add("X-Test-Market", "SA");

        var resp = await reporterClient.PostAsJsonAsync($"/api/customer/reviews/{reviewId}/report",
            new ReportReviewRequest("not_a_real_reason", null));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be(ReviewReasonCode.ReportReasonInvalid);
    }

    private async Task<(HttpClient client, Guid customerId, Guid reviewId)> SubmitVisibleReviewAsync()
    {
        var client = _factory.CreateClient();
        var customerId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Test-Customer-Id", customerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", "SA");

        var resp = await client.PostAsJsonAsync("/api/customer/reviews",
            new SubmitReviewRequest(Guid.NewGuid(), 5, "Headline",
                "Clean body content here long enough.", "en", null));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var reviewId = body.GetProperty("id").GetGuid();
        return (client, customerId, reviewId);
    }
}
