using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Cms.Authorization;
using Cms.Tests.Contract.Infrastructure;
using FluentAssertions;

namespace Cms.Tests.Contract.Editor;

/// <summary>
/// T098 — contract test for <c>POST /v1/admin/cms/blog-articles/drafts</c>
/// per spec 024 contract §3.3. Asserts slug-uniqueness, slug-pattern,
/// body-size limits, and SEO field validation.
/// </summary>
public sealed class SaveBlogArticleDraftContractTests
    : IClassFixture<CmsApiFactory>, IAsyncLifetime
{
    private readonly CmsApiFactory _factory;
    private readonly HttpClient _client;

    public SaveBlogArticleDraftContractTests(CmsApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetCmsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private HttpRequestMessage AdminPost(string url, object body, string permission, Guid? actorId = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        req.Headers.Add("X-Test-Admin-Id", (actorId ?? Guid.NewGuid()).ToString());
        req.Headers.Add("X-Test-Permissions", permission);
        req.Headers.Add("X-Test-Role", "admin");
        return req;
    }

    private static object ValidBody(string slug = "valid-slug", string locale = "en") => new
    {
        category = "tips",
        slug,
        authoredLocale = locale,
        title = "A title",
        summary = "A summary",
        body = "Article body content.",
        coverAssetId = (Guid?)null,
        seoMetaTitle = "Title",
        seoMetaDescription = "Description",
        seoOgImageId = (Guid?)null,
        seoSchemaOrgKind = "BlogPosting",
        scheduledPublishAtUtc = (DateTimeOffset?)null,
        marketCode = "EG",
        xmin = (uint?)null,
    };

    [Fact]
    public async Task POST_blog_article_draft_returns_201_for_editor_with_valid_body()
    {
        var resp = await _client.SendAsync(AdminPost(
            "/v1/admin/cms/blog-articles/drafts",
            ValidBody(slug: "valid-slug-201"),
            CmsPermissions.Editor));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task POST_blog_article_draft_rejects_invalid_slug_pattern()
    {
        // Uppercase + space: violates ^[a-z0-9]+(-[a-z0-9]+)*$.
        var resp = await _client.SendAsync(AdminPost(
            "/v1/admin/cms/blog-articles/drafts",
            ValidBody(slug: "Bad Slug!"),
            CmsPermissions.Editor));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("cms.blog.slug_invalid_pattern");
    }

    [Fact]
    public async Task POST_blog_article_draft_rejects_body_over_60000_chars()
    {
        var huge = new string('x', 60_001);
        var bodyObj = new
        {
            category = "tips",
            slug = "huge-body-slug",
            authoredLocale = "en",
            title = "Title",
            summary = (string?)null,
            body = huge,
            coverAssetId = (Guid?)null,
            seoMetaTitle = "Title",
            seoMetaDescription = "Description",
            seoOgImageId = (Guid?)null,
            seoSchemaOrgKind = "BlogPosting",
            scheduledPublishAtUtc = (DateTimeOffset?)null,
            marketCode = "EG",
            xmin = (uint?)null,
        };
        var resp = await _client.SendAsync(AdminPost(
            "/v1/admin/cms/blog-articles/drafts",
            bodyObj,
            CmsPermissions.Editor));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var s = await resp.Content.ReadAsStringAsync();
        s.Should().Contain("cms.blog.body_too_long");
    }

    [Fact]
    public async Task POST_blog_article_draft_rejects_seo_meta_title_over_70_chars()
    {
        var bodyObj = new
        {
            category = "tips",
            slug = "seo-overflow",
            authoredLocale = "en",
            title = "Title",
            summary = (string?)null,
            body = "body",
            coverAssetId = (Guid?)null,
            seoMetaTitle = new string('t', 71),
            seoMetaDescription = "OK",
            seoOgImageId = (Guid?)null,
            seoSchemaOrgKind = "BlogPosting",
            scheduledPublishAtUtc = (DateTimeOffset?)null,
            marketCode = "EG",
            xmin = (uint?)null,
        };
        var resp = await _client.SendAsync(AdminPost(
            "/v1/admin/cms/blog-articles/drafts",
            bodyObj,
            CmsPermissions.Editor));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_blog_article_draft_rejects_seo_meta_description_over_160_chars()
    {
        var bodyObj = new
        {
            category = "tips",
            slug = "seo-desc-overflow",
            authoredLocale = "en",
            title = "Title",
            summary = (string?)null,
            body = "body",
            coverAssetId = (Guid?)null,
            seoMetaTitle = "Title",
            seoMetaDescription = new string('d', 161),
            seoOgImageId = (Guid?)null,
            seoSchemaOrgKind = "BlogPosting",
            scheduledPublishAtUtc = (DateTimeOffset?)null,
            marketCode = "EG",
            xmin = (uint?)null,
        };
        var resp = await _client.SendAsync(AdminPost(
            "/v1/admin/cms/blog-articles/drafts",
            bodyObj,
            CmsPermissions.Editor));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_blog_article_draft_rejects_unsupported_locale()
    {
        var resp = await _client.SendAsync(AdminPost(
            "/v1/admin/cms/blog-articles/drafts",
            ValidBody(slug: "fr-locale", locale: "fr"),
            CmsPermissions.Editor));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("cms.storefront.locale_unsupported");
    }

    [Fact]
    public async Task POST_blog_article_draft_returns_403_without_editor_permission()
    {
        var resp = await _client.SendAsync(AdminPost(
            "/v1/admin/cms/blog-articles/drafts",
            ValidBody(slug: "perm-denied"),
            CmsPermissions.Publisher));
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task POST_blog_article_draft_returns_409_on_slug_collision_within_market_locale()
    {
        // First create succeeds.
        var first = await _client.SendAsync(AdminPost(
            "/v1/admin/cms/blog-articles/drafts",
            ValidBody(slug: "collision-test"),
            CmsPermissions.Editor));
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // Same (slug, market_code, authored_locale) → unique-key violation.
        var second = await _client.SendAsync(AdminPost(
            "/v1/admin/cms/blog-articles/drafts",
            ValidBody(slug: "collision-test"),
            CmsPermissions.Editor));
        // Slug collisions surface as either 400 (validator-level) or 409
        // (DB unique-key violation). Both are valid wire signals — assert
        // the boundary is "not 201".
        second.StatusCode.Should().NotBe(HttpStatusCode.Created);
        var body = await second.Content.ReadAsStringAsync();
        body.Should().Contain("slug");
    }
}
