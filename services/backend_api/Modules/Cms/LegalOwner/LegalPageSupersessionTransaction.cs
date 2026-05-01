using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.LegalOwner;

/// <summary>
/// Promotes a legal page version <c>{draft|scheduled} → live</c> AND, in the
/// same DB transaction, supersedes the prior <c>live</c> version of the same
/// <c>(legal_page_kind, market_code)</c>. The partial unique index
/// <c>UX_cms_legal_one_live_per_kind_market</c> on
/// <c>cms.legal_page_versions</c> guarantees only one row can be in
/// <c>live</c> at a time per pair — concurrent publish attempts serialize on
/// the index; the loser surfaces a typed
/// <see cref="LegalPagePublisherResult"/> conflict (never a 500).
///
/// Idempotency: callers re-running the same publish after a partial commit
/// see no prior-live row to supersede; the new row is already <c>live</c>;
/// the method short-circuits and returns the loaded row as-is.
/// </summary>
public sealed class LegalPageSupersessionTransaction
{
    /// <summary>
    /// Result tuple. <see cref="NewLiveRow"/> is the freshly promoted row;
    /// <see cref="SupersededRow"/> is the prior row (if any) flipped to
    /// <c>superseded</c> with <c>superseded_at_utc</c> + <c>superseded_by_version_id</c>
    /// stamped.
    /// </summary>
    public sealed record SupersessionOutcome(
        LegalPageVersion NewLiveRow,
        LegalPageVersion? SupersededRow,
        bool AlreadyLiveOnEntry);

    /// <summary>
    /// Runs the atomic promotion + prior-live supersession.
    /// Caller MUST already have asserted: locale-completeness, RBAC, target
    /// row exists, source state ∈ {draft,scheduled}.
    /// </summary>
    public async Task<SupersessionOutcome> ExecuteAsync(
        CmsDbContext db,
        LegalPageVersion newRow,
        DateTimeOffset nowUtc,
        CmsActorKind actorKind,
        string triggerKind,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(newRow);

        var liveWire = ContentLifecycleState.Live.ToWire();
        var supersededWire = ContentLifecycleState.Superseded.ToWire();

        // If the row is already live (re-entry / replay), no-op.
        if (newRow.StateWire == liveWire)
        {
            return new SupersessionOutcome(newRow, null, AlreadyLiveOnEntry: true);
        }

        var sourceState = ContentLifecycleStateWire.FromWire(newRow.StateWire);

        // Compile-time guard the new transition.
        CmsContentLifecycle.AssertCanTransition(
            sourceState, ContentLifecycleState.Live,
            EntityKind.LegalPageVersion, triggerKind, actorKind);

        // Open a SERIALIZABLE-style scope using an explicit transaction. We
        // additionally take a Postgres advisory lock keyed on
        // (legal_page_kind, market_code) so two concurrent publishers can't
        // interleave the load-prior + flip-states sequence even though the
        // partial unique index would still catch them at commit time.
        await using var tx = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);

        // Advisory lock keyed on (kind, market) hash. Two-int form so
        // pg_advisory_xact_lock takes (int4,int4) — released on tx end.
        var (k1, k2) = HashLockKey(newRow.LegalPageKindWire, newRow.MarketCode);
        await db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0}, {1});",
            new object[] { k1, k2 }, ct);

        // Load the prior live row (if any). The advisory lock above already
        // serializes concurrent transactions on (kind, market); the partial
        // unique index on cms.legal_page_versions is the second-level guard
        // that turns any remaining race into a typed unique-violation 409.
        var trackedPrior = await db.LegalPageVersions
            .FirstOrDefaultAsync(v =>
                v.LegalPageKindWire == newRow.LegalPageKindWire &&
                v.MarketCode == newRow.MarketCode &&
                v.StateWire == liveWire &&
                v.Id != newRow.Id, ct);

        if (trackedPrior is not null)
        {
            // The prior-row supersede is a system-orchestrated side-effect of
            // the publish, not an action the caller performs directly — so the
            // lifecycle guard expects actor=System on this edge regardless of
            // who triggered the parent publish.
            CmsContentLifecycle.AssertCanTransition(
                ContentLifecycleState.Live, ContentLifecycleState.Superseded,
                EntityKind.LegalPageVersion,
                CmsTriggerKind.WorkerSupersedeLegalVersion, CmsActorKind.System);
            trackedPrior.StateWire = supersededWire;
            trackedPrior.SupersededAtUtc = nowUtc;
            trackedPrior.SupersededByVersionId = newRow.Id;

            // Flush the prior → superseded update first so the partial unique
            // index UX_cms_legal_one_live_per_kind_market is no longer
            // contended when we promote the new row in the next flush. Both
            // updates are still inside the same SERIALIZABLE transaction.
            await db.SaveChangesAsync(ct);
        }

        // Promote the new row.
        var trackedNew = await db.LegalPageVersions
            .FirstOrDefaultAsync(v => v.Id == newRow.Id, ct);
        if (trackedNew is null)
        {
            await tx.RollbackAsync(ct);
            throw new InvalidOperationException(
                $"LegalPageVersion {newRow.Id} not found in supersession transaction.");
        }
        trackedNew.StateWire = liveWire;
        trackedNew.PublishedAtUtc = nowUtc;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new SupersessionOutcome(
            trackedNew,
            trackedPrior?.StateWire == supersededWire ? trackedPrior : null,
            AlreadyLiveOnEntry: false);
    }

    /// <summary>Stable two-int advisory-lock key derived from (kind, market).</summary>
    public static (int K1, int K2) HashLockKey(string kind, string market)
    {
        const int Prefix = 0x024_C5_00; // 2_279_680
        // FNV-1a 32-bit on the joined input.
        var data = string.Concat(kind, "|", market);
        unchecked
        {
            int h = (int)2166136261u;
            foreach (var c in data)
            {
                h ^= c;
                h *= 16777619;
            }
            return (Prefix, h);
        }
    }
}
