using BackendApi.Modules.Reviews.Entities;
using BackendApi.Modules.Reviews.Primitives;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Reviews.Tests.Infrastructure;

namespace Reviews.Tests.Integration.Persistence;

/// <summary>
/// Spec 022 T051 — verifies the BEFORE UPDATE OR DELETE triggers on the 3
/// append-only tables raise SQLSTATE 23000.
/// </summary>
[Collection(nameof(ReviewsPostgresCollection))]
public sealed class AppendOnlyTriggersTests
{
    private readonly ReviewsPostgresFixture _fx;

    public AppendOnlyTriggersTests(ReviewsPostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Update_on_review_moderation_decisions_raises_immutable_violation()
    {
        var reviewId = await SeedMinimalReviewAsync();

        await using var ctx = _fx.NewContext();
        ctx.ModerationDecisions.Add(new ReviewModerationDecision
        {
            Id = Guid.NewGuid(),
            ReviewId = reviewId,
            ActorId = Guid.NewGuid(),
            ActorRole = "customer",
            FromState = ReviewState.Visible,
            ToState = ReviewState.Visible,
            TriggeredBy = ReviewTriggerKind.CustomerSubmission,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();

        // Direct UPDATE bypassing EF — trigger fires on raw DML.
        await using var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE reviews.review_moderation_decisions SET \"ReasonNote\" = 'tamper' WHERE \"ReviewId\" = @r;", conn);
        cmd.Parameters.AddWithValue("r", reviewId);

        var act = async () => await cmd.ExecuteNonQueryAsync();
        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23000");
    }

    [Fact]
    public async Task Delete_on_review_admin_notes_raises_immutable_violation()
    {
        var reviewId = await SeedMinimalReviewAsync();

        await using var ctx = _fx.NewContext();
        ctx.AdminNotes.Add(new ReviewAdminNote
        {
            Id = Guid.NewGuid(),
            ReviewId = reviewId,
            ActorId = Guid.NewGuid(),
            Note = "Initial note for trigger test.",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();

        await using var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM reviews.review_admin_notes WHERE \"ReviewId\" = @r;", conn);
        cmd.Parameters.AddWithValue("r", reviewId);

        var act = async () => await cmd.ExecuteNonQueryAsync();
        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23000");
    }

    [Fact]
    public async Task Update_on_review_flags_raises_immutable_violation()
    {
        var reviewId = await SeedMinimalReviewAsync();

        await using var ctx = _fx.NewContext();
        ctx.Flags.Add(new ReviewFlag
        {
            Id = Guid.NewGuid(),
            ReviewId = reviewId,
            ReporterActorId = Guid.NewGuid(),
            Reason = "spam_or_irrelevant",
            IsQualified = true,
            QualifyingEvaluationJson = "{}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();

        await using var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE reviews.review_flags SET \"IsQualified\" = false WHERE \"ReviewId\" = @r;", conn);
        cmd.Parameters.AddWithValue("r", reviewId);

        var act = async () => await cmd.ExecuteNonQueryAsync();
        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23000");
    }

    private async Task<Guid> SeedMinimalReviewAsync()
    {
        await using var ctx = _fx.NewContext();
        var review = new Review
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            OrderLineId = Guid.NewGuid(),
            MarketCode = "SA",
            Rating = 5,
            Headline = "trigger-test",
            Body = "Body text long enough to satisfy the CHECK constraint.",
            Locale = "en",
            MediaUrlsJson = "[]",
            State = ReviewState.Visible,
            StateChangedAtUtc = DateTimeOffset.UtcNow,
            StateChangedByActorId = Guid.NewGuid(),
            TriggeredBy = ReviewTriggerKind.CustomerSubmission,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            DeliveredAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
        };
        ctx.Reviews.Add(review);
        await ctx.SaveChangesAsync();
        return review.Id;
    }
}
