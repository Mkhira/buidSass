using System.Text.RegularExpressions;
using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Seeding;
using Cms.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cms.Tests.Integration.Seeding;

/// <summary>
/// T137 — CI integration: <c>seed-pii-guard</c> smoke test against the seeded
/// CMS dataset. Asserts no real-phone, real-email, or national-ID patterns
/// appear in any human-readable text column. Synthetic dataset only — the
/// dev seeder is non-production by design (gated by SeedGuard) but a
/// regression that injects production-looking PII into seed data would leak
/// through staging snapshots and is forbidden.
/// </summary>
[Collection(nameof(CmsPostgresCollection))]
public sealed class CmsV1DevSeederPiiGuardTests
{
    private readonly CmsPostgresFixture _fx;

    public CmsV1DevSeederPiiGuardTests(CmsPostgresFixture fx)
    {
        _fx = fx;
    }

    // ── PII patterns ─────────────────────────────────────────────────────
    // Phone: any 9–15-digit run with optional +/() separators that LOOKS
    // like an Egyptian or Saudi mobile prefix. We deliberately match
    // permissively to flag any plausibly-real number.
    private static readonly Regex EgyptianMobilePattern = new(
        @"\+?20\s*1[0125]\d{8}|\b01[0125]\d{8}\b",
        RegexOptions.Compiled);
    private static readonly Regex SaudiMobilePattern = new(
        @"\+?966\s*5\d{8}|\b05\d{8}\b",
        RegexOptions.Compiled);

    // Email: any RFC-5322-ish local@domain. Matches against `@example`,
    // `@test`, etc. — those are the synthetic markers we DO allow. So we
    // assert any matched email is contained in an allow-list of synthetic
    // domains.
    private static readonly Regex EmailPattern = new(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}",
        RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedSyntheticEmailDomains =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "example.com", "example.org", "example.net",
            "test", "test.local", "localhost",
            "dental-commerce.local", "dental-commerce.test",
        };

    // National ID:
    //   • Egypt: 14 digits starting with 2 or 3.
    //   • KSA: 10 digits starting with 1 or 2.
    private static readonly Regex EgyptNationalIdPattern = new(
        @"\b[23]\d{13}\b", RegexOptions.Compiled);
    private static readonly Regex SaudiNationalIdPattern = new(
        @"\b[12]\d{9}\b", RegexOptions.Compiled);

    [Fact]
    public async Task Seeded_dataset_contains_no_real_phone_email_or_national_id_patterns()
    {
        var ct = CancellationToken.None;
        await _fx.ResetAsync();

        var connStr = _fx.ConnectionString;
        var services = new ServiceCollection()
            .AddDbContext<CmsDbContext>(opt => opt.UseNpgsql(connStr))
            .BuildServiceProvider();

        await using (var scope = services.CreateAsyncScope())
        {
            var seedCtx = new SeedContext(
                Db: null!,
                Services: scope.ServiceProvider,
                Size: DatasetSize.Small,
                Env: new TestHostEnvForPii(),
                Logger: NullLogger.Instance);
            await new CmsReferenceDataSeeder().ApplyAsync(seedCtx, ct);
            await new CmsV1DevSeeder().ApplyAsync(seedCtx, ct);
        }

        // Pull every human-readable string column from every CMS entity
        // and concatenate. Smaller than dumping the entire DB to text.
        var corpus = new List<string>();
        await using var verify = _fx.NewContext();

        foreach (var b in await verify.BannerSlots.AsNoTracking().ToListAsync(ct))
        {
            corpus.Add(b.HeadlineEn ?? "");
            corpus.Add(b.HeadlineAr ?? "");
            corpus.Add(b.SubheadEn ?? "");
            corpus.Add(b.SubheadAr ?? "");
            corpus.Add(b.ArchiveReasonNote ?? "");
        }
        foreach (var f in await verify.FeaturedSections.AsNoTracking().ToListAsync(ct))
        {
            corpus.Add(f.TitleEn ?? "");
            corpus.Add(f.TitleAr ?? "");
            corpus.Add(f.SubtitleEn ?? "");
            corpus.Add(f.SubtitleAr ?? "");
            corpus.Add(f.ArchiveReasonNote ?? "");
        }
        foreach (var faq in await verify.FaqEntries.AsNoTracking().ToListAsync(ct))
        {
            corpus.Add(faq.QuestionEn ?? "");
            corpus.Add(faq.QuestionAr ?? "");
            corpus.Add(faq.AnswerEn ?? "");
            corpus.Add(faq.AnswerAr ?? "");
        }
        foreach (var blog in await verify.BlogArticles.AsNoTracking().ToListAsync(ct))
        {
            corpus.Add(blog.Title ?? "");
            corpus.Add(blog.Summary ?? "");
            corpus.Add(blog.Body ?? "");
            corpus.Add(blog.SeoMetaTitle ?? "");
            corpus.Add(blog.SeoMetaDescription ?? "");
            corpus.Add(blog.Slug ?? "");
        }
        foreach (var legal in await verify.LegalPageVersions.AsNoTracking().ToListAsync(ct))
        {
            corpus.Add(legal.BodyEn ?? "");
            corpus.Add(legal.BodyAr ?? "");
            corpus.Add(legal.VersionLabel ?? "");
        }

        var bigText = string.Join("\n", corpus);

        // ── Phone numbers: zero matches.
        var egPhones = EgyptianMobilePattern.Matches(bigText);
        var ksaPhones = SaudiMobilePattern.Matches(bigText);
        egPhones.Count.Should().Be(0,
            $"seed dataset must not contain Egyptian mobile patterns (matched: " +
            $"{string.Join(", ", egPhones.Select(m => m.Value))})");
        ksaPhones.Count.Should().Be(0,
            $"seed dataset must not contain Saudi mobile patterns (matched: " +
            $"{string.Join(", ", ksaPhones.Select(m => m.Value))})");

        // ── National IDs: zero matches.
        // The legal-page version labels (v1.0 / v2.0) and FAQ display orders
        // do not collide with the patterns (require 10 / 14 contiguous digits).
        var egIds = EgyptNationalIdPattern.Matches(bigText);
        var ksaIds = SaudiNationalIdPattern.Matches(bigText);
        egIds.Count.Should().Be(0,
            $"seed dataset must not contain Egypt national-ID-shaped digits (matched: " +
            $"{string.Join(", ", egIds.Select(m => m.Value))})");
        ksaIds.Count.Should().Be(0,
            $"seed dataset must not contain KSA national-ID-shaped digits (matched: " +
            $"{string.Join(", ", ksaIds.Select(m => m.Value))})");

        // ── Emails: any match must be in an explicitly-synthetic domain.
        foreach (Match m in EmailPattern.Matches(bigText))
        {
            var atIdx = m.Value.IndexOf('@');
            atIdx.Should().BePositive();
            var domain = m.Value[(atIdx + 1)..];
            AllowedSyntheticEmailDomains.Should().Contain(domain,
                $"seed dataset email '{m.Value}' is not in an allowed synthetic domain");
        }
    }

    private sealed class TestHostEnvForPii : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Cms.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
