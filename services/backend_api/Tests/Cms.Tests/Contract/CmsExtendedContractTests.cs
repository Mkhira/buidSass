using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Cms.Authorization;
using BackendApi.Modules.Cms.Entities;
using Cms.Tests.Contract.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Cms.Tests.Contract;

/// <summary>
/// Second-batch contract tests covering the remaining slices: PATCH xmin
/// conflict (T057), schedule-publish gates (T058), featured-section save
/// validation (T115), FAQ save (T124), bulk-reorder conflict (T125),
/// blog single-locale storefront semantics (T100), preview signature
/// invalid (T101), super-admin metrics (T168 partial).
/// </summary>
public sealed class CmsExtendedContractTests : IClassFixture<CmsApiFactory>, IAsyncLifetime
{
    private readonly CmsApiFactory _factory;
    private readonly HttpClient _client;

    public CmsExtendedContractTests(CmsApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetCmsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private HttpRequestMessage AdminPost(string url, object? body, string permission, Guid? actorId = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        req.Headers.Add("X-Test-Admin-Id", (actorId ?? Guid.NewGuid()).ToString());
        req.Headers.Add("X-Test-Permissions", permission);
        req.Headers.Add("X-Test-Role", "admin");
        return req;
    }

    private HttpRequestMessage AdminPatch(string url, object body, string permission, Guid? actorId = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Patch, url) { Content = JsonContent.Create(body) };
        req.Headers.Add("X-Test-Admin-Id", (actorId ?? Guid.NewGuid()).ToString());
        req.Headers.Add("X-Test-Permissions", permission);
        req.Headers.Add("X-Test-Role", "admin");
        return req;
    }

    private HttpRequestMessage AdminGet(string url, string permission, Guid? actorId = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-Test-Admin-Id", (actorId ?? Guid.NewGuid()).ToString());
        req.Headers.Add("X-Test-Permissions", permission);
        req.Headers.Add("X-Test-Role", "admin");
        return req;
    }

    [Fact]
    public async Task PATCH_banner_draft_with_stale_xmin_returns_409()
    {
        // Create a banner.
        var createBody = new { slotKind = "hero_top", marketCode = "EG", ctaKind = "none" };
        var create = await _client.SendAsync(AdminPost(
            "/v1/admin/cms/banner-slots/drafts", createBody, CmsPermissions.Editor));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        // PATCH with a wrong xmin → 409.
        var patch = await _client.SendAsync(AdminPatch(
            $"/v1/admin/cms/banner-slots/drafts/{id}",
            new { slotKind = "hero_top", marketCode = "EG", ctaKind = "none", xmin = 99999u },
            CmsPermissions.Editor));
        patch.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await patch.Content.ReadAsStringAsync();
        body.Should().Contain("cms.draft.version_conflict");
    }

    [Fact]
    public async Task POST_banner_schedule_publish_requires_locale_completeness()
    {
        await using var ctx = _factory.NewDbContext();
        var nowUtc = DateTimeOffset.UtcNow;
        var bannerId = Guid.NewGuid();
        ctx.BannerSlots.Add(new BannerSlot
        {
            Id = bannerId,
            SlotKindWire = "hero_top",
            HeadlineEn = null,
            HeadlineAr = null,
            CtaKindWire = "none",
            MarketCode = "EG",
            StateWire = "draft",
            CtaHealthWire = "not_applicable",
            PriorityWithinSlot = 100,
            OwnerActorId = Guid.NewGuid(),
            CreatedAtUtc = nowUtc,
            EditorSaveAtUtc = nowUtc,
            ScheduledStartUtc = nowUtc.AddDays(1),
            ScheduledEndUtc = nowUtc.AddDays(8),
        });
        await ctx.SaveChangesAsync();

        var resp = await _client.SendAsync(AdminPost(
            $"/v1/admin/cms/banner-slots/{bannerId}/schedule-publish", null, CmsPermissions.Publisher));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("cms.publish.locale_completeness_missing");
    }

    [Fact]
    public async Task POST_featured_section_draft_rejects_empty_references()
    {
        var body = new
        {
            sectionKind = "home_top",
            titleAr = "أ",
            titleEn = "T",
            references = Array.Empty<object>(),
            displayPriority = (int?)null,
            marketCode = "EG",
        };
        var resp = await _client.SendAsync(AdminPost(
            "/v1/admin/cms/featured-sections/drafts", body, CmsPermissions.Editor));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var s = await resp.Content.ReadAsStringAsync();
        s.Should().Contain("cms.featured_section.empty_references");
    }

    [Fact]
    public async Task POST_featured_section_draft_rejects_unsupported_reference_kind()
    {
        var body = new
        {
            sectionKind = "home_top",
            titleAr = "أ",
            titleEn = "T",
            references = new[] { new { kind = "vendor", id = Guid.NewGuid() } },
            displayPriority = (int?)null,
            marketCode = "EG",
        };
        var resp = await _client.SendAsync(AdminPost(
            "/v1/admin/cms/featured-sections/drafts", body, CmsPermissions.Editor));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var s = await resp.Content.ReadAsStringAsync();
        s.Should().Contain("cms.featured_section.reference_kind_unsupported");
    }

    [Fact]
    public async Task POST_faq_entry_draft_returns_201()
    {
        var body = new
        {
            category = "ordering",
            questionAr = "كيف؟",
            questionEn = "How?",
            answerAr = "هكذا.",
            answerEn = "Like this.",
            displayOrder = (int?)100,
            marketCode = "EG",
        };
        var resp = await _client.SendAsync(AdminPost(
            "/v1/admin/cms/faq-entries/drafts", body, CmsPermissions.Editor));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task POST_faq_reorder_with_stale_xmin_returns_409()
    {
        // Seed two faq entries.
        await using var ctx = _factory.NewDbContext();
        var nowUtc = DateTimeOffset.UtcNow;
        var faq1 = new FaqEntry
        {
            Id = Guid.NewGuid(), CategoryWire = "ordering", QuestionEn = "q1", QuestionAr = "س",
            AnswerEn = "a", AnswerAr = "ج", MarketCode = "EG", StateWire = "live",
            DisplayOrder = 1, OwnerActorId = Guid.NewGuid(), CreatedAtUtc = nowUtc, EditorSaveAtUtc = nowUtc,
        };
        ctx.FaqEntries.Add(faq1);
        await ctx.SaveChangesAsync();

        var body = new
        {
            entries = new[] { new { id = faq1.Id, displayOrder = 5, xmin = 999999u } },
        };
        var resp = await _client.SendAsync(AdminPost(
            "/v1/admin/cms/faq-entries/reorder", body, CmsPermissions.Editor));
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var s = await resp.Content.ReadAsStringAsync();
        s.Should().Contain("cms.faq.reorder_conflict");
    }

    [Fact]
    public async Task GET_storefront_blog_article_signals_locale_unavailability_for_single_locale_articles()
    {
        await using var ctx = _factory.NewDbContext();
        var nowUtc = DateTimeOffset.UtcNow;
        ctx.BlogArticles.Add(new BlogArticle
        {
            Id = Guid.NewGuid(),
            CategoryWire = "tips",
            Slug = "ar-only-tip",
            AuthoredLocale = "ar",
            Title = "نصيحة",
            Summary = "مختصر",
            Body = "النص",
            MarketCode = "EG",
            StateWire = "live",
            SeoMetaTitle = "نصيحة",
            SeoMetaDescription = "مختصر",
            SeoSchemaOrgKind = "BlogPosting",
            OwnerActorId = Guid.NewGuid(),
            CreatedAtUtc = nowUtc,
            EditorSaveAtUtc = nowUtc,
            PublishedAtUtc = nowUtc,
        });
        await ctx.SaveChangesAsync();

        var resp = await _client.GetAsync(
            "/v1/storefront/cms/blog-articles/ar-only-tip?market=EG&locale=en");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.Content.ReadAsStringAsync();
        json.Should().Contain("\"localizationUnavailableForRequestedLocale\":true");
        // Body should be null when requested locale != authored locale.
        json.Should().Contain("\"body\":null");
        json.Should().Contain("\"availableLocales\":[\"ar\"]");
    }

    [Fact]
    public async Task GET_preview_with_invalid_token_returns_401()
    {
        var resp = await _client.GetAsync(
            $"/v1/storefront/cms/preview/banner_slot/{Guid.NewGuid()}?token=not-a-valid-token");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("cms.preview.token_signature_invalid");
    }

    [Fact]
    public async Task GET_metrics_returns_distribution_for_super_admin()
    {
        var resp = await _client.SendAsync(AdminGet(
            "/v1/admin/cms/metrics", "super_admin"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.Content.ReadAsStringAsync();
        json.Should().Contain("\"banner_slot\"");
        json.Should().Contain("\"legal_page_version\"");
        json.Should().Contain("\"preview_tokens\"");
    }

    [Fact]
    public async Task GET_metrics_returns_403_without_finance_or_super_admin()
    {
        var resp = await _client.SendAsync(AdminGet(
            "/v1/admin/cms/metrics", CmsPermissions.Editor));
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
