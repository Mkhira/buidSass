using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Primitives;
using Cms.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cms.Tests.Integration.LegalOwner;

/// <summary>
/// T087 — assert that the migration's BEFORE DELETE trigger on
/// cms.legal_page_versions raises cms.legal_page.version.delete_forbidden
/// for every state. (The API endpoint also fail-closes with a 405; this
/// suite exercises the DB-level guarantee directly.)
/// </summary>
[Collection(nameof(CmsPostgresCollection))]
public sealed class LegalPageHardDeleteForbiddenTests
{
    private readonly CmsPostgresFixture _fx;

    public LegalPageHardDeleteForbiddenTests(CmsPostgresFixture fx)
    {
        _fx = fx;
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("scheduled")]
    [InlineData("live")]
    [InlineData("superseded")]
    public async Task Trigger_blocks_delete_in_every_state(string state)
    {
        var ct = CancellationToken.None;
        await _fx.ResetAsync();
        var nowUtc = DateTimeOffset.UtcNow;

        await using var ctx = _fx.NewContext();
        var row = new LegalPageVersion
        {
            Id = Guid.NewGuid(),
            LegalPageKindWire = "cookies",
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

        Func<Task> deleteAct = async () =>
        {
            await using var del = _fx.NewContext();
            await del.LegalPageVersions
                .Where(v => v.Id == row.Id)
                .ExecuteDeleteAsync(ct);
        };

        var ex = await deleteAct.Should().ThrowAsync<Exception>();
        // Either Npgsql or EF surfaces the DB exception; we only need to
        // verify the trigger fired with the expected reason marker.
        var combined = ex.Subject.First().ToString();
        combined.Should().Contain("delete_forbidden",
            "the BEFORE DELETE trigger raises a Postgres exception with the cms.legal_page.version.delete_forbidden marker");
    }
}
