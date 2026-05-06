using BackendApi.Features.Seeding;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BackendApi.Modules.B2B.Seeding;

/// <summary>
/// Spec 021 task T145. Dev-only synthetic dataset that exercises every state of
/// the B2B state machines (Company, Membership, Invitation, Quote) so demos and
/// manual QA don't force the operator to drive transitions by hand.
///
/// <para>Hard-gated: <see cref="SeedGuard"/> blocks Production; this seeder also
/// short-circuits if the host environment isn't Development. Idempotent — re-runs
/// are no-ops once the synthetic rows exist (we key off a stable company id).</para>
///
/// <para>Seeded surface:</para>
/// <list type="bullet">
///   <item>3 companies — one active with <c>approver_required=true</c> + 2 approvers,
///         one active with <c>approver_required=false</c>, one in
///         <c>pending-verification</c> state.</item>
///   <item>2 branches on the approver-required company (HQ + Riyadh North).</item>
///   <item>6 memberships across the three companies (admin/buyer/approver).</item>
///   <item>4 invitations (one per <see cref="CompanyInvitationState"/>).</item>
///   <item>8 quotes — one per <see cref="QuoteState"/>.</item>
///   <item>2 repeat-order templates (one anchored to an accepted quote, one anchored
///         to the same template-source under a different name).</item>
/// </list>
/// </summary>
public sealed class B2BDevDataSeeder : ISeeder
{
    public string Name => "b2b.dev-data";
    public int Version => 1;
    public IReadOnlyList<string> DependsOn => ["b2b.reference-data"];

    private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    // Stable canary id — presence on a re-run signals "already seeded".
    private static readonly Guid CanaryCompanyId = new("b2b00000-0000-0000-0000-000000000001");

    public async Task ApplyAsync(SeedContext ctx, CancellationToken ct)
    {
        if (!ctx.Env.IsDevelopment())
        {
            return;
        }

        var db = ctx.Services.GetRequiredService<B2BDbContext>();

        var alreadySeeded = await db.Companies
            .AsNoTracking()
            .AnyAsync(c => c.Id == CanaryCompanyId, ct);
        if (alreadySeeded)
        {
            return;
        }

        // Wrap every staged save in one transaction so a partial failure rolls back
        // the canary company. Without this, a mid-run crash would leave the canary
        // present and every subsequent rerun would short-circuit to a no-op,
        // permanently freezing the dev dataset in a half-seeded state.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        SeedCompanies(db);
        SeedBranches(db);
        SeedMemberships(db);
        SeedInvitations(db);

        // Quotes need company FKs to be persisted first.
        await db.SaveChangesAsync(ct);

        SeedQuotes(db);
        await db.SaveChangesAsync(ct);

        SeedTemplates(db);
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }

    private static void SeedCompanies(B2BDbContext db)
    {
        db.Companies.AddRange(
            new Company
            {
                Id = CanaryCompanyId,
                MarketCode = "ksa",
                NameJson = "{\"en\":\"Riyadh Dental Group\",\"ar\":\"مجموعة الرياض لطب الأسنان\"}",
                TaxId = "300000000000001",
                PrimaryAddressJson = "{\"city\":\"Riyadh\",\"country\":\"SA\"}",
                BillingAddressJson = null,
                ApproverRequired = true,
                PoRequired = true,
                UniquePoRequired = true,
                InvoiceBillingEligible = true,
                State = "active",
                CreatedAt = BaseTime,
                UpdatedAt = BaseTime,
            },
            new Company
            {
                Id = new Guid("b2b00000-0000-0000-0000-000000000002"),
                MarketCode = "ksa",
                NameJson = "{\"en\":\"Jeddah Smile Clinic\",\"ar\":\"عيادة جدة للابتسامة\"}",
                TaxId = "300000000000002",
                PrimaryAddressJson = "{\"city\":\"Jeddah\",\"country\":\"SA\"}",
                BillingAddressJson = null,
                ApproverRequired = false,
                PoRequired = false,
                UniquePoRequired = false,
                InvoiceBillingEligible = true,
                State = "active",
                CreatedAt = BaseTime,
                UpdatedAt = BaseTime,
            },
            new Company
            {
                Id = new Guid("b2b00000-0000-0000-0000-000000000003"),
                MarketCode = "eg",
                NameJson = "{\"en\":\"Cairo Implant Center\",\"ar\":\"مركز القاهرة للزراعة\"}",
                TaxId = "200000000000003",
                PrimaryAddressJson = "{\"city\":\"Cairo\",\"country\":\"EG\"}",
                BillingAddressJson = null,
                ApproverRequired = true,
                PoRequired = false,
                UniquePoRequired = false,
                InvoiceBillingEligible = false,
                State = "pending-verification",
                CreatedAt = BaseTime,
                UpdatedAt = BaseTime,
            });
    }

    private static void SeedBranches(B2BDbContext db)
    {
        db.CompanyBranches.AddRange(
            new CompanyBranch
            {
                Id = new Guid("b2b00010-0000-0000-0000-000000000001"),
                CompanyId = CanaryCompanyId,
                MarketCode = "ksa",
                NameJson = "{\"en\":\"Headquarters\",\"ar\":\"المقر الرئيسي\"}",
                AddressJson = "{\"city\":\"Riyadh\",\"country\":\"SA\"}",
            },
            new CompanyBranch
            {
                Id = new Guid("b2b00010-0000-0000-0000-000000000002"),
                CompanyId = CanaryCompanyId,
                MarketCode = "ksa",
                NameJson = "{\"en\":\"Riyadh North\",\"ar\":\"الرياض الشمالية\"}",
                AddressJson = "{\"city\":\"Riyadh\",\"country\":\"SA\"}",
            });
    }

    private static void SeedMemberships(B2BDbContext db)
    {
        // Approver-required company: 1 admin, 1 buyer, 2 approvers.
        db.CompanyMemberships.AddRange(
            Membership(new("b2b00020-0000-0000-0000-000000000001"), CanaryCompanyId, "ksa", new("c2b00000-0000-0000-0000-000000000001"), "companies.admin"),
            Membership(new("b2b00020-0000-0000-0000-000000000002"), CanaryCompanyId, "ksa", new("c2b00000-0000-0000-0000-000000000002"), "buyer"),
            Membership(new("b2b00020-0000-0000-0000-000000000003"), CanaryCompanyId, "ksa", new("c2b00000-0000-0000-0000-000000000003"), "approver"),
            Membership(new("b2b00020-0000-0000-0000-000000000004"), CanaryCompanyId, "ksa", new("c2b00000-0000-0000-0000-000000000004"), "approver"),
            // Approver-not-required company: solo admin.
            Membership(new("b2b00020-0000-0000-0000-000000000005"), new("b2b00000-0000-0000-0000-000000000002"), "ksa", new("c2b00000-0000-0000-0000-000000000005"), "companies.admin"),
            // Pending-verification company: solo admin.
            Membership(new("b2b00020-0000-0000-0000-000000000006"), new("b2b00000-0000-0000-0000-000000000003"), "eg", new("c2b00000-0000-0000-0000-000000000006"), "companies.admin"));
    }

    private static CompanyMembership Membership(Guid id, Guid companyId, string market, Guid userId, string role) => new()
    {
        Id = id,
        CompanyId = companyId,
        MarketCode = market,
        UserId = userId,
        Role = role,
        JoinedAt = BaseTime,
    };

    private static void SeedInvitations(B2BDbContext db)
    {
        db.CompanyInvitations.AddRange(
            Invitation(new("b2b00030-0000-0000-0000-000000000001"), "pending", BaseTime.AddDays(7), "pending-1@example.test"),
            Invitation(new("b2b00030-0000-0000-0000-000000000002"), "accepted", BaseTime.AddDays(-1), "accepted-1@example.test"),
            Invitation(new("b2b00030-0000-0000-0000-000000000003"), "declined", BaseTime.AddDays(-1), "declined-1@example.test"),
            Invitation(new("b2b00030-0000-0000-0000-000000000004"), "expired", BaseTime.AddDays(-1), "expired-1@example.test"));
    }

    private static CompanyInvitation Invitation(Guid id, string state, DateTimeOffset expiresAt, string email) => new()
    {
        Id = id,
        CompanyId = CanaryCompanyId,
        MarketCode = "ksa",
        InvitedBy = new Guid("c2b00000-0000-0000-0000-000000000001"),
        InvitedEmail = email,
        TargetRole = "buyer",
        TokenHash = "dev-seed-hash-" + id.ToString("N"),
        State = state,
        SentAt = BaseTime.AddDays(-7),
        ExpiresAt = expiresAt,
    };

    private static void SeedQuotes(B2BDbContext db)
    {
        var customer = new Guid("c2b00000-0000-0000-0000-000000000002");
        var states = new[]
        {
            ("requested",       Guid.Parse("b2b00040-0000-0000-0000-000000000001")),
            ("drafted",         Guid.Parse("b2b00040-0000-0000-0000-000000000002")),
            ("revised",         Guid.Parse("b2b00040-0000-0000-0000-000000000003")),
            ("pending-approver",Guid.Parse("b2b00040-0000-0000-0000-000000000004")),
            ("accepted",        Guid.Parse("b2b00040-0000-0000-0000-000000000005")),
            ("rejected",        Guid.Parse("b2b00040-0000-0000-0000-000000000006")),
            ("expired",         Guid.Parse("b2b00040-0000-0000-0000-000000000007")),
            ("withdrawn",       Guid.Parse("b2b00040-0000-0000-0000-000000000008")),
        };

        foreach (var (state, id) in states)
        {
            var isTerminal = QuoteStateExtensions.TryParseToken(state, out var s) && s.IsTerminal();
            db.Quotes.Add(new Quote
            {
                Id = id,
                CustomerId = customer,
                CompanyId = CanaryCompanyId,
                BranchId = null,
                MarketCode = "ksa",
                State = state,
                RequestedAt = BaseTime,
                ExpiresAt = state == "expired" ? BaseTime.AddDays(-1) : BaseTime.AddDays(14),
                TerminalAt = isTerminal ? BaseTime.AddDays(1) : (DateTimeOffset?)null,
                TerminalReason = isTerminal ? state : null,
                CustomerSuppliedMessageJson = null,
                RestrictionPolicySnapshotJson = "{}",
                SchemaVersion = 1,
                PoNumber = $"PO-DEV-{state}-{id.ToString("N")[24..]}",
            });
            db.QuoteStateTransitions.Add(new QuoteStateTransition
            {
                Id = Guid.NewGuid(),
                QuoteId = id,
                MarketCode = "ksa",
                PriorState = "__none__",
                NewState = state,
                ActorKind = QuoteActorKind.System.ToToken(),
                ActorId = null,
                ReasonJson = null,
                MetadataJson = "{\"source\":\"dev-seed\"}",
                OccurredAt = BaseTime,
            });
        }
    }

    private static void SeedTemplates(B2BDbContext db)
    {
        var acceptedQuoteId = new Guid("b2b00040-0000-0000-0000-000000000005");
        var ownerId = new Guid("c2b00000-0000-0000-0000-000000000002");

        db.RepeatOrderTemplates.AddRange(
            new RepeatOrderTemplate
            {
                Id = new("b2b00050-0000-0000-0000-000000000001"),
                SourceQuoteId = acceptedQuoteId,
                CompanyId = CanaryCompanyId,
                UserId = ownerId,
                MarketCode = "ksa",
                NameJson = "{\"en\":\"Monthly Restock\",\"ar\":\"التزويد الشهري\"}",
                CreatedAt = BaseTime,
                CreatedBy = ownerId,
            },
            new RepeatOrderTemplate
            {
                Id = new("b2b00050-0000-0000-0000-000000000002"),
                SourceQuoteId = acceptedQuoteId,
                CompanyId = CanaryCompanyId,
                UserId = ownerId,
                MarketCode = "ksa",
                NameJson = "{\"en\":\"Quarterly Implants\",\"ar\":\"الزراعة الفصلية\"}",
                CreatedAt = BaseTime,
                CreatedBy = ownerId,
            });
    }
}
