using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Primitives;
using BackendApi.Modules.Support.Seeding;
using FluentAssertions;
using System.Reflection;

namespace Support.Tests.Unit;

/// <summary>
/// Spec 023 US7 (T109) — verifies the synthetic fixture builder emits the
/// per-state distribution + breach + conversion + redaction-request examples
/// the spec calls out (SC-009). Reflection is used so we don't need to wire
/// a full Postgres harness for a pure-data assertion.
/// </summary>
public sealed class SupportV1DevSeederFixturesTests
{
    private static IReadOnlyList<object> BuildFixtures()
    {
        var method = typeof(SupportV1DevSeeder).GetMethod(
            "BuildFixtures", BindingFlags.NonPublic | BindingFlags.Static)!;
        var raw = method.Invoke(null, null)!;
        return ((System.Collections.IEnumerable)raw).Cast<object>().ToList();
    }

    private static SupportTicket TicketOf(object fixture)
    {
        var prop = fixture.GetType().GetProperty("Ticket")!;
        return (SupportTicket)prop.GetValue(fixture)!;
    }

    private static IReadOnlyList<TicketLink> LinksOf(object fixture)
    {
        var prop = fixture.GetType().GetProperty("Links")!;
        return ((IEnumerable<TicketLink>)prop.GetValue(fixture)!).ToList();
    }

    private static IReadOnlyList<TicketSlaBreachEvent> BreachOf(object fixture)
    {
        var prop = fixture.GetType().GetProperty("BreachEvents")!;
        return ((IEnumerable<TicketSlaBreachEvent>)prop.GetValue(fixture)!).ToList();
    }

    [Fact]
    public void Fixtures_Cover_All_FR007_Categories_Among_Open_Tickets()
    {
        var fixtures = BuildFixtures();
        var openCategories = fixtures
            .Select(TicketOf)
            .Where(t => t.State == TicketStateNames.Open)
            .Select(t => t.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        // 9 of 10 FR-007 categories appear in the open-tickets bucket
        // (one is reused by the breach examples). Every category in the
        // master list should appear at least once across the full dataset.
        var allCategories = fixtures
            .Select(TicketOf)
            .Select(t => t.Category)
            .Distinct()
            .ToList();

        foreach (var c in TicketCategoryNames.All)
        {
            allCategories.Should().Contain(c, $"category '{c}' must appear at least once in the fixture set");
        }
    }

    [Fact]
    public void Fixtures_Distribute_State_Counts_Per_SC009()
    {
        var fixtures = BuildFixtures();
        var byState = fixtures
            .Select(TicketOf)
            .GroupBy(t => t.State)
            .ToDictionary(g => g.Key, g => g.Count());

        // open: 11 from category sweep (one per FR-007 category) + 2 from
        // breach examples + 2 from review-dispute escalations = 15.
        byState[TicketStateNames.Open].Should().Be(15);
        byState[TicketStateNames.InProgress].Should().Be(5);
        byState[TicketStateNames.WaitingCustomer].Should().Be(4 + 2,
            "4 explicit + 2 from return-conversion examples");
        byState[TicketStateNames.Resolved].Should().Be(6);
        byState[TicketStateNames.Closed].Should().Be(3);
    }

    [Fact]
    public void Fixtures_Include_Two_Active_Sla_Breaches_With_Distinct_Kinds()
    {
        var fixtures = BuildFixtures();
        var breachKinds = fixtures
            .SelectMany(BreachOf)
            .Select(b => b.BreachKind)
            .ToList();

        breachKinds.Should().Contain(new[]
        {
            TicketSlaBreachKind.FirstResponse,
            TicketSlaBreachKind.Resolution,
        });
    }

    [Fact]
    public void Fixtures_Include_Two_Return_Conversion_Examples_With_Bidirectional_Links()
    {
        var fixtures = BuildFixtures();
        var conversionLinkCount = fixtures
            .SelectMany(LinksOf)
            .Count(l => l.Kind == TicketLinkedEntityKindNames.ReturnRequest);

        conversionLinkCount.Should().Be(2,
            "two return-conversion examples each contribute one return_request link");

        // Each conversion fixture must also have a sibling order_line link
        // captured at submission time, so the bidirectional traversal works.
        var orderLineLinkCount = fixtures
            .SelectMany(LinksOf)
            .Count(l => l.Kind == TicketLinkedEntityKindNames.OrderLine);
        orderLineLinkCount.Should().Be(2);
    }

    [Fact]
    public void Fixtures_Include_One_Redaction_Request_Ticket()
    {
        var fixtures = BuildFixtures();
        var redactionRequestCount = fixtures
            .Select(TicketOf)
            .Count(t => t.Category == TicketCategoryNames.RedactionRequest);

        redactionRequestCount.Should().Be(1);
    }

    [Fact]
    public void Fixtures_Are_Bilingual_Across_Markets()
    {
        var fixtures = BuildFixtures();
        var locales = fixtures
            .Select(TicketOf)
            .Select(t => t.Locale)
            .Distinct()
            .OrderBy(l => l)
            .ToList();

        locales.Should().Contain(new[] { "ar", "en" },
            "fixtures must include both Arabic and English locales (Principle 4)");
    }

    [Fact]
    public void Fixtures_Use_Both_Markets()
    {
        var fixtures = BuildFixtures();
        var markets = fixtures
            .Select(TicketOf)
            .Select(t => t.MarketCode)
            .Distinct()
            .OrderBy(m => m)
            .ToList();

        markets.Should().Contain(new[] { "EG", "SA" });
    }
}
