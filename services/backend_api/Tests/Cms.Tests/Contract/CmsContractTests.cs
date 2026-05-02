using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Cms.Authorization;
using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Primitives;
using Cms.Tests.Contract.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Cms.Tests.Contract;

/// <summary>
/// Contract tests for spec 024 HTTP surface, run against a Testcontainers
/// Postgres + the full WebApplicationFactory pipeline. Covers the most
/// impactful endpoints from T056 / T071 / T085 / T087-API / T088 / T099
/// (banner draft + ETag + legal page CRUD + storefront substitution +
/// preview-token round-trip).
/// </summary>
public sealed class CmsContractTests : IClassFixture<CmsApiFactory>, IAsyncLifetime
{
    private readonly CmsApiFactory _factory;
    private readonly HttpClient _client;

    public CmsContractTests(CmsApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetCmsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private HttpRequestMessage AdminPost(string url, object body, string permission, Guid? actorId = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.Add("X-Test-Admin-Id", (actorId ?? Guid.NewGuid()).ToString());
        req.Headers.Add("X-Test-Permissions", permission);
        req.Headers.Add("X-Test-Role", "admin");
        return req;
    }

    private HttpRequestMessage AdminDelete(string url, string permission, Guid? actorId = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, url);
        req.Headers.Add("X-Test-Admin-Id", (actorId ?? Guid.NewGuid()).ToString());
        req.Headers.Add("X-Test-Permissions", permission);
        req.Headers.Add("X-Test-Role", "admin");
        return req;
    }

    [Fact]
    public async Task POST_banner_draft_returns_201_with_xmin()
    {
        var body = new
        {
            slotKind = "hero_top",
            headlineAr = "بانر",
            headlineEn = "Banner",
            subheadAr = (string?)null,
            subheadEn = (string?)null,
            assetIdAr = (Guid?)null,
            assetIdEn = (Guid?)null,
            ctaKind = "none",
            ctaTarget = (string?)null,
            scheduledStartUtc = (DateTimeOffset?)null,
            scheduledEndUtc = (DateTimeOffset?)null,
            marketCode = "EG",
            priorityWithinSlot = (int?)null,
            xmin = (uint?)null,
        };
        var req = AdminPost("/v1/admin/cms/banner-slots/drafts", body, CmsPermissions.Editor);
        var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task POST_banner_draft_returns_403_without_editor_permission()
    {
        var body = new { slotKind = "hero_top", marketCode = "EG", ctaKind = "none" };
        var req = AdminPost("/v1/admin/cms/banner-slots/drafts", body, "cms.publisher");
        var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task POST_legal_page_draft_returns_201_for_legal_owner()
    {
        var body = new
        {
            versionLabel = "v1.0",
            bodyAr = "نص",
            bodyEn = "text",
            effectiveAtUtc = DateTimeOffset.UtcNow.AddDays(1),
            marketCode = "EG",
            xmin = (uint?)null,
        };
        var req = AdminPost("/v1/admin/cms/legal-pages/privacy/versions/drafts", body, CmsPermissions.LegalOwner);
        var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task DELETE_legal_page_version_returns_405_in_every_state()
    {
        // Seed each state via direct DB insert.
        await using var ctx = _factory.NewDbContext();
        var nowUtc = DateTimeOffset.UtcNow;
        var ids = new Dictionary<string, Guid>();
        foreach (var state in new[] { "draft", "scheduled", "live", "superseded" })
        {
            var id = Guid.NewGuid();
            ids[state] = id;
            ctx.LegalPageVersions.Add(new LegalPageVersion
            {
                Id = id,
                LegalPageKindWire = "terms",
                VersionLabel = $"v-{state}",
                BodyAr = "ع",
                BodyEn = "en",
                EffectiveAtUtc = nowUtc,
                MarketCode = state == "live" ? "EG" : (state == "scheduled" ? "KSA" : "*"),
                StateWire = state,
                OwnerActorId = Guid.NewGuid(),
                CreatedAtUtc = nowUtc,
                EditorSaveAtUtc = nowUtc,
                PublishedAtUtc = state is "live" or "superseded" ? nowUtc : null,
                SupersededAtUtc = state == "superseded" ? nowUtc : null,
                SupersededByVersionId = state == "superseded" ? Guid.NewGuid() : null,
            });
        }
        await ctx.SaveChangesAsync();

        foreach (var (state, id) in ids)
        {
            var req = AdminDelete($"/v1/admin/cms/legal-pages/terms/versions/{id}", CmsPermissions.LegalOwner);
            var resp = await _client.SendAsync(req);
            resp.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed,
                $"hard-delete forbidden for state {state}");
        }
    }

    [Fact]
    public async Task GET_storefront_legal_page_substitutes_star_when_market_specific_missing()
    {
        await using var ctx = _factory.NewDbContext();
        var nowUtc = DateTimeOffset.UtcNow;
        // Only a `*`-scoped live row.
        ctx.LegalPageVersions.Add(new LegalPageVersion
        {
            Id = Guid.NewGuid(),
            LegalPageKindWire = "cookies",
            VersionLabel = "v1.0",
            BodyAr = "نسخة شاملة",
            BodyEn = "global version",
            EffectiveAtUtc = nowUtc.AddDays(-1),
            MarketCode = "*",
            StateWire = "live",
            OwnerActorId = Guid.NewGuid(),
            CreatedAtUtc = nowUtc.AddDays(-2),
            EditorSaveAtUtc = nowUtc.AddDays(-2),
            PublishedAtUtc = nowUtc.AddDays(-1),
        });
        await ctx.SaveChangesAsync();

        var resp = await _client.GetAsync("/v1/storefront/cms/legal-pages/cookies?market=EG&locale=en");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.Content.ReadAsStringAsync();
        json.Should().Contain("global version");
    }

    [Fact]
    public async Task GET_storefront_legal_page_returns_404_when_no_substitution()
    {
        var resp = await _client.GetAsync("/v1/storefront/cms/legal-pages/privacy?market=EG&locale=ar");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("cms.legal_page.not_found_for_market");
    }

    [Fact]
    public async Task GET_storefront_banners_returns_etag_and_304_on_match()
    {
        await using var ctx = _factory.NewDbContext();
        var nowUtc = DateTimeOffset.UtcNow;
        ctx.BannerSlots.Add(new BannerSlot
        {
            Id = Guid.NewGuid(),
            SlotKindWire = "hero_top",
            HeadlineEn = "Hello",
            HeadlineAr = "أهلًا",
            CtaKindWire = "none",
            MarketCode = "EG",
            StateWire = "live",
            CtaHealthWire = "not_applicable",
            PriorityWithinSlot = 100,
            OwnerActorId = Guid.NewGuid(),
            CreatedAtUtc = nowUtc,
            EditorSaveAtUtc = nowUtc,
            PublishedAtUtc = nowUtc,
        });
        await ctx.SaveChangesAsync();

        var first = await _client.GetAsync("/v1/storefront/cms/banner-slots?market=EG&locale=en");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Headers.ETag.Should().NotBeNull();
        var etag = first.Headers.ETag!.ToString();

        var second = new HttpRequestMessage(HttpMethod.Get, "/v1/storefront/cms/banner-slots?market=EG&locale=en");
        second.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var notModified = await _client.SendAsync(second);
        notModified.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task GET_storefront_banner_returns_400_on_unsupported_market()
    {
        var resp = await _client.GetAsync("/v1/storefront/cms/banner-slots?market=FR&locale=en");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("cms.storefront.market_unsupported");
    }

    [Fact]
    public async Task POST_preview_token_for_banner_returns_token_then_revoke_returns_200()
    {
        // Seed a banner draft.
        await using var ctx = _factory.NewDbContext();
        var nowUtc = DateTimeOffset.UtcNow;
        var bannerId = Guid.NewGuid();
        ctx.BannerSlots.Add(new BannerSlot
        {
            Id = bannerId,
            SlotKindWire = "hero_top",
            HeadlineEn = "draft",
            HeadlineAr = "مسودة",
            CtaKindWire = "none",
            MarketCode = "EG",
            StateWire = "draft",
            CtaHealthWire = "not_applicable",
            PriorityWithinSlot = 100,
            OwnerActorId = Guid.NewGuid(),
            CreatedAtUtc = nowUtc,
            EditorSaveAtUtc = nowUtc,
        });
        await ctx.SaveChangesAsync();

        var mintReq = AdminPost(
            $"/v1/admin/cms/banner_slot/{bannerId}/preview-token",
            new { ttlHours = (int?)1 },
            CmsPermissions.Editor);
        var mintResp = await _client.SendAsync(mintReq);
        mintResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var minted = await mintResp.Content.ReadFromJsonAsync<JsonElement>();
        var tokenHashHex = minted.GetProperty("tokenHashHex").GetString();
        var token = minted.GetProperty("token").GetString();
        tokenHashHex.Should().NotBeNullOrEmpty();
        token.Should().NotBeNullOrEmpty();

        // Read the previewed entity.
        var readResp = await _client.GetAsync(
            $"/v1/storefront/cms/preview/banner_slot/{bannerId}?token={Uri.EscapeDataString(token!)}");
        readResp.StatusCode.Should().Be(HttpStatusCode.OK);
        readResp.Headers.GetValues("X-Robots-Tag")
            .Should().Contain(v => v.Contains("noindex"));

        // Revoke is idempotent.
        var revokeReq = AdminDelete(
            $"/v1/admin/cms/preview-token/{tokenHashHex}", CmsPermissions.Editor);
        var revokeResp = await _client.SendAsync(revokeReq);
        revokeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Subsequent read returns 401.
        var readAfter = await _client.GetAsync(
            $"/v1/storefront/cms/preview/banner_slot/{bannerId}?token={Uri.EscapeDataString(token!)}");
        readAfter.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
