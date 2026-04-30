using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Reviews.Customer.SubmitReview;
using BackendApi.Modules.Reviews.Primitives;
using BackendApi.Modules.Reviews.RateLimit;
using FluentAssertions;
using Reviews.Tests.Contract.Infrastructure;

namespace Reviews.Tests.Contract;

/// <summary>
/// Spec 022 T053 + T067 — HTTP contract for POST /api/customer/reviews:
/// status codes, problem-details shape with stable <c>reasonCode</c> extension,
/// auth gating, validation errors, filter-trip path. Wire-shape only — handler
/// business behavior is covered by handler-level tests.
/// </summary>
public sealed class SubmitReviewContractTests : IClassFixture<ReviewsApiFactory>
{
    private readonly ReviewsApiFactory _factory;

    public SubmitReviewContractTests(ReviewsApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Unauthenticated_request_returns_401()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/customer/reviews",
            new SubmitReviewRequest(Guid.NewGuid(), 5, "h", "body content here long enough", "en", null));
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Happy_path_returns_201_with_visible_state_and_row_version()
    {
        var client = _factory.CreateClient();
        var customerId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Test-Customer-Id", customerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", "SA");

        var resp = await client.PostAsJsonAsync("/api/customer/reviews",
            new SubmitReviewRequest(Guid.NewGuid(), 5, "Headline",
                "Body content of sufficient length.", "en", null));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("state").GetString().Should().Be("visible");
        body.GetProperty("pendingReview").GetBoolean().Should().BeFalse();
        body.GetProperty("rowVersion").ValueKind.Should().NotBe(JsonValueKind.Undefined);
    }

    [Fact]
    public async Task Filter_trip_returns_201_with_pending_moderation_state()
    {
        var client = _factory.CreateClient();
        var customerId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Test-Customer-Id", customerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", "SA");

        var resp = await client.PostAsJsonAsync("/api/customer/reviews",
            new SubmitReviewRequest(Guid.NewGuid(), 4, "headline",
                "This product is spam pretending to be useful.", "en", null));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("state").GetString().Should().Be("pending_moderation");
        body.GetProperty("pendingReview").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Validation_failure_returns_400_problem_details_with_reason_code()
    {
        var client = _factory.CreateClient();
        var customerId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Test-Customer-Id", customerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", "SA");

        // rating outside [1,5]
        var resp = await client.PostAsJsonAsync("/api/customer/reviews",
            new SubmitReviewRequest(Guid.NewGuid(), 99, "Headline",
                "Body content here.", "en", null));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetInt32().Should().Be(400);
        body.GetProperty("reasonCode").GetString().Should().Be(ReviewReasonCode.RatingOutOfRange);
    }

    [Fact]
    public async Task Rate_limit_exceeded_returns_429_with_spec_reason_code()
    {
        var client = _factory.CreateClient();
        var customerId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Test-Customer-Id", customerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", "SA");

        // Burn through the 5/hour customer cap.
        for (var i = 0; i < ReviewRateLimits.CustomerCapacityPerHour; i++)
        {
            await client.PostAsJsonAsync("/api/customer/reviews",
                new SubmitReviewRequest(Guid.NewGuid(), 5, $"H{i}",
                    $"Body content for submission {i}.", "en", null));
        }

        var rejection = await client.PostAsJsonAsync("/api/customer/reviews",
            new SubmitReviewRequest(Guid.NewGuid(), 5, "Headline",
                "Body content here.", "en", null));
        rejection.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var body = await rejection.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be(ReviewReasonCode.RateLimitSubmissionExceeded);
    }
}
