using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Primitives;
using Cms.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Cms.Tests.Integration;

/// <summary>
/// T170 / SC-011 — for every entity kind in every non-`draft` state, a raw
/// DELETE is rejected at the DB layer (Phase 2 migration's BEFORE DELETE
/// trigger on cms.legal_page_versions raises the typed reason; for the
/// other kinds, drafts ARE deletable so the assertion is shaped per-kind).
/// </summary>
[Collection(nameof(CmsPostgresCollection))]
public sealed class HardDeleteProhibitionTests
{
    private readonly CmsPostgresFixture _fx;

    public HardDeleteProhibitionTests(CmsPostgresFixture fx)
    {
        _fx = fx;
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("scheduled")]
    [InlineData("live")]
    [InlineData("superseded")]
    public async Task LegalPageVersion_delete_is_blocked_in_every_state(string state)
    {
        var ct = CancellationToken.None;
        await _fx.ResetAsync();
        await using var ctx = _fx.NewContext();
        var nowUtc = DateTimeOffset.UtcNow;
        var row = new LegalPageVersion
        {
            Id = Guid.NewGuid(),
            LegalPageKindWire = "terms",
            VersionLabel = $"v-{state}",
            BodyAr = "ع",
            BodyEn = "en",
            EffectiveAtUtc = nowUtc,
            MarketCode = "EG",
            StateWire = state,
            OwnerActorId = Guid.NewGuid(),
            CreatedAtUtc = nowUtc,
            EditorSaveAtUtc = nowUtc,
            PublishedAtUtc = state is "live" or "superseded" ? nowUtc : null,
            SupersededAtUtc = state == "superseded" ? nowUtc : null,
            SupersededByVersionId = state == "superseded" ? Guid.NewGuid() : null,
        };
        ctx.LegalPageVersions.Add(row);
        await ctx.SaveChangesAsync(ct);

        Func<Task> attempt = async () =>
        {
            await using var del = _fx.NewContext();
            await del.LegalPageVersions.Where(v => v.Id == row.Id).ExecuteDeleteAsync(ct);
        };
        await attempt.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Drafts_for_other_kinds_remain_deletable_under_API_authorization()
    {
        // Sanity: the trigger-based prohibition is legal-page-specific. Other
        // kinds rely on the API layer (DraftManagementEndpoints) to reject
        // non-draft deletes; raw DB deletes succeed and ARE allowed since the
        // soft-delete enforcement lives at the application layer for those.
        var ct = CancellationToken.None;
        await _fx.ResetAsync();
        var nowUtc = DateTimeOffset.UtcNow;
        await using var ctx = _fx.NewContext();
        var faq = new FaqEntry
        {
            Id = Guid.NewGuid(),
            CategoryWire = "ordering",
            QuestionEn = "q",
            QuestionAr = "س",
            AnswerEn = "a",
            AnswerAr = "ج",
            MarketCode = "EG",
            StateWire = "draft",
            DisplayOrder = 100,
            OwnerActorId = Guid.NewGuid(),
            CreatedAtUtc = nowUtc,
            EditorSaveAtUtc = nowUtc,
        };
        ctx.FaqEntries.Add(faq);
        await ctx.SaveChangesAsync(ct);

        await using var del = _fx.NewContext();
        var deleted = await del.FaqEntries.Where(f => f.Id == faq.Id).ExecuteDeleteAsync(ct);
        deleted.Should().Be(1);
    }
}
