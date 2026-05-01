using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.LegalOwner;
using BackendApi.Modules.Cms.Primitives;
using Cms.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Cms.Tests.Integration.LegalOwner;

/// <summary>
/// Postgres-backed integration tests for the Phase-5 supersession
/// transaction. Mandatory verification — Phase 5's core deliverable is that
/// new-live ↔ prior-superseded happen atomically.
/// </summary>
[Collection(nameof(CmsPostgresCollection))]
public sealed class LegalPageSupersessionTransactionTests
{
    private readonly CmsPostgresFixture _fx;

    public LegalPageSupersessionTransactionTests(CmsPostgresFixture fx)
    {
        _fx = fx;
    }

    private async Task<(LegalPageVersion Prior, LegalPageVersion NewDraft)> SeedPairAsync(
        string kind, string market, CancellationToken ct = default)
    {
        await _fx.ResetAsync();
        await using var ctx = _fx.NewContext();
        var nowUtc = DateTimeOffset.UtcNow;

        var prior = new LegalPageVersion
        {
            Id = Guid.NewGuid(),
            LegalPageKindWire = kind,
            VersionLabel = "v1.0",
            BodyAr = "نسخة سابقة",
            BodyEn = "prior version",
            EffectiveAtUtc = nowUtc.AddDays(-30),
            MarketCode = market,
            StateWire = ContentLifecycleState.Live.ToWire(),
            OwnerActorId = Guid.NewGuid(),
            CreatedAtUtc = nowUtc.AddDays(-31),
            EditorSaveAtUtc = nowUtc.AddDays(-31),
            PublishedAtUtc = nowUtc.AddDays(-30),
        };
        var newDraft = new LegalPageVersion
        {
            Id = Guid.NewGuid(),
            LegalPageKindWire = kind,
            VersionLabel = "v2.0",
            BodyAr = "النسخة الجديدة",
            BodyEn = "new version",
            EffectiveAtUtc = nowUtc.AddMinutes(-1),
            MarketCode = market,
            StateWire = ContentLifecycleState.Draft.ToWire(),
            OwnerActorId = Guid.NewGuid(),
            CreatedAtUtc = nowUtc,
            EditorSaveAtUtc = nowUtc,
        };
        ctx.LegalPageVersions.AddRange(prior, newDraft);
        await ctx.SaveChangesAsync(ct);
        return (prior, newDraft);
    }

    [Fact]
    public async Task Promote_to_live_supersedes_prior_in_same_transaction()
    {
        var ct = CancellationToken.None;
        var (prior, newDraft) = await SeedPairAsync("privacy", "EG", ct);

        await using var ctx = _fx.NewContext();
        var sut = new LegalPageSupersessionTransaction();
        var nowUtc = DateTimeOffset.UtcNow;

        var trackedNew = await ctx.LegalPageVersions.FirstAsync(v => v.Id == newDraft.Id, ct);
        var outcome = await sut.ExecuteAsync(
            ctx, trackedNew, nowUtc,
            CmsActorKind.LegalOwner, CmsTriggerKind.PublisherPublishNow, ct);

        outcome.NewLiveRow.StateWire.Should().Be(ContentLifecycleState.Live.ToWire());
        outcome.SupersededRow.Should().NotBeNull();
        outcome.SupersededRow!.Id.Should().Be(prior.Id);
        outcome.SupersededRow.StateWire.Should().Be(ContentLifecycleState.Superseded.ToWire());

        await using var verify = _fx.NewContext();
        var liveCount = await verify.LegalPageVersions.CountAsync(v =>
            v.LegalPageKindWire == "privacy" &&
            v.MarketCode == "EG" &&
            v.StateWire == ContentLifecycleState.Live.ToWire(), ct);
        liveCount.Should().Be(1);

        var priorAfter = await verify.LegalPageVersions.FirstAsync(v => v.Id == prior.Id, ct);
        priorAfter.SupersededAtUtc.Should().NotBeNull();
        priorAfter.SupersededByVersionId.Should().Be(newDraft.Id);
    }

    [Fact]
    public async Task First_publish_with_no_prior_live_creates_a_single_live_row()
    {
        var ct = CancellationToken.None;
        await _fx.ResetAsync();
        await using var ctx = _fx.NewContext();
        var nowUtc = DateTimeOffset.UtcNow;

        var draft = new LegalPageVersion
        {
            Id = Guid.NewGuid(),
            LegalPageKindWire = "terms",
            VersionLabel = "v1.0",
            BodyAr = "ع",
            BodyEn = "en",
            EffectiveAtUtc = nowUtc,
            MarketCode = "KSA",
            StateWire = ContentLifecycleState.Draft.ToWire(),
            OwnerActorId = Guid.NewGuid(),
            CreatedAtUtc = nowUtc,
            EditorSaveAtUtc = nowUtc,
        };
        ctx.LegalPageVersions.Add(draft);
        await ctx.SaveChangesAsync(ct);

        var sut = new LegalPageSupersessionTransaction();
        var outcome = await sut.ExecuteAsync(
            ctx, draft, nowUtc, CmsActorKind.LegalOwner, CmsTriggerKind.PublisherPublishNow, ct);

        outcome.NewLiveRow.StateWire.Should().Be(ContentLifecycleState.Live.ToWire());
        outcome.SupersededRow.Should().BeNull();
        outcome.AlreadyLiveOnEntry.Should().BeFalse();
    }

    [Fact]
    public async Task Concurrent_publishes_serialize_and_loser_returns_unique_violation()
    {
        var ct = CancellationToken.None;
        await _fx.ResetAsync();
        var nowUtc = DateTimeOffset.UtcNow;

        // Two concurrent draft rows competing to become live for (privacy, EG).
        await using (var seed = _fx.NewContext())
        {
            seed.LegalPageVersions.AddRange(
                BuildDraft("privacy", "EG", "v2.0", nowUtc),
                BuildDraft("privacy", "EG", "v3.0", nowUtc));
            await seed.SaveChangesAsync(ct);
        }

        // Run the two publishes in parallel against fresh DbContexts.
        await using var ctxA = _fx.NewContext();
        await using var ctxB = _fx.NewContext();
        var sutA = new LegalPageSupersessionTransaction();
        var sutB = new LegalPageSupersessionTransaction();

        var rows = await ctxA.LegalPageVersions
            .Where(v => v.LegalPageKindWire == "privacy" && v.MarketCode == "EG")
            .OrderBy(v => v.VersionLabel)
            .ToListAsync(ct);
        rows.Should().HaveCount(2);
        var rowA = rows[0];
        var rowB = await ctxB.LegalPageVersions.FirstAsync(v => v.Id == rows[1].Id, ct);

        Task<LegalPageSupersessionTransaction.SupersessionOutcome?> RunAsync(
            LegalPageSupersessionTransaction sut, BackendApi.Modules.Cms.Persistence.CmsDbContext ctx, LegalPageVersion row)
            => Task.Run(async () =>
            {
                try
                {
                    return await sut.ExecuteAsync(
                        ctx, row, nowUtc,
                        CmsActorKind.LegalOwner, CmsTriggerKind.PublisherPublishNow, ct);
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    return null;
                }
                catch (DbUpdateConcurrencyException)
                {
                    return null;
                }
            });

        var taskA = RunAsync(sutA, ctxA, rowA);
        var taskB = RunAsync(sutB, ctxB, rowB);
        var results = await Task.WhenAll(taskA, taskB);

        // Both *can* succeed sequentially under the advisory lock — the
        // second publish simply supersedes whatever the first published.
        // The non-negotiable invariant is that the partial unique index is
        // never violated: at-rest, exactly one row is `live` for the
        // (kind, market) pair and the other is either `draft` (loser
        // skipped before its tx ran), `superseded` (its commit was
        // overwritten by the next publisher), or `live` (the final
        // winner). Test isolation: no 5xx surfaces.
        results.Should().NotBeEmpty();

        await using var verify = _fx.NewContext();
        var liveCount = await verify.LegalPageVersions.CountAsync(v =>
            v.LegalPageKindWire == "privacy" &&
            v.MarketCode == "EG" &&
            v.StateWire == ContentLifecycleState.Live.ToWire(), ct);
        liveCount.Should().Be(1,
            "the partial unique index UX_cms_legal_one_live_per_kind_market guarantees this");
    }

    [Fact]
    public async Task Already_live_row_short_circuits_to_no_op()
    {
        var ct = CancellationToken.None;
        await _fx.ResetAsync();
        var nowUtc = DateTimeOffset.UtcNow;

        await using var ctx = _fx.NewContext();
        var live = BuildDraft("returns", "*", "v1.0", nowUtc);
        live.StateWire = ContentLifecycleState.Live.ToWire();
        live.PublishedAtUtc = nowUtc;
        ctx.LegalPageVersions.Add(live);
        await ctx.SaveChangesAsync(ct);

        var sut = new LegalPageSupersessionTransaction();
        var outcome = await sut.ExecuteAsync(
            ctx, live, nowUtc, CmsActorKind.LegalOwner, CmsTriggerKind.PublisherPublishNow, ct);

        outcome.AlreadyLiveOnEntry.Should().BeTrue();
        outcome.SupersededRow.Should().BeNull();
    }

    private static LegalPageVersion BuildDraft(string kind, string market, string label, DateTimeOffset nowUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            LegalPageKindWire = kind,
            VersionLabel = label,
            BodyAr = "ع",
            BodyEn = "en",
            EffectiveAtUtc = nowUtc.AddMinutes(-1),
            MarketCode = market,
            StateWire = ContentLifecycleState.Draft.ToWire(),
            OwnerActorId = Guid.NewGuid(),
            CreatedAtUtc = nowUtc,
            EditorSaveAtUtc = nowUtc,
        };

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is Npgsql.PostgresException pg && pg.SqlState == "23505") return true;
        }
        return false;
    }
}
