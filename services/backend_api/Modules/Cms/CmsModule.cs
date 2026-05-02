using BackendApi.Configuration;
using BackendApi.Features.Seeding;
using BackendApi.Modules.Cms.Editor;
using BackendApi.Modules.Cms.Editor.BulkReorderFaqEntries;
using BackendApi.Modules.Cms.Editor.SaveBannerDraft;
using BackendApi.Modules.Cms.Editor.SaveBlogArticleDraft;
using BackendApi.Modules.Cms.Editor.SaveFaqEntryDraft;
using BackendApi.Modules.Cms.Editor.SaveFeaturedSectionDraft;
using BackendApi.Modules.Cms.LegalOwner;
using BackendApi.Modules.Cms.LegalOwner.ListLegalPageVersionHistory;
using BackendApi.Modules.Cms.LegalOwner.PublishLegalPageVersionNow;
using BackendApi.Modules.Cms.LegalOwner.SaveLegalPageVersionDraft;
using BackendApi.Modules.Cms.LegalOwner.SchedulePublishLegalPageVersion;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using BackendApi.Modules.Cms.Publisher;
using BackendApi.Modules.Cms.Preview;
using BackendApi.Modules.Cms.Preview.MintPreviewToken;
using BackendApi.Modules.Cms.Preview.ReadPreviewedDraft;
using BackendApi.Modules.Cms.Preview.RevokePreviewToken;
using BackendApi.Modules.Cms.Publisher.PublishNow;
using BackendApi.Modules.Cms.Storefront;
using BackendApi.Modules.Cms.Storefront.GetBlogArticle;
using BackendApi.Modules.Cms.Storefront.GetLegalPage;
using BackendApi.Modules.Cms.Storefront.ListBannerSlots;
using BackendApi.Modules.Cms.Storefront.ListBlogArticles;
using BackendApi.Modules.Cms.Storefront.ListFaqEntries;
using BackendApi.Modules.Cms.Storefront.ListFeaturedSections;
using BackendApi.Modules.Cms.Seeding;
using BackendApi.Modules.Cms.Services;
using BackendApi.Modules.Cms.Subscribers;
using BackendApi.Modules.Cms.SuperAdmin;
using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace BackendApi.Modules.Cms;

/// <summary>
/// DI + endpoint wiring for the CMS vertical-slice module per spec 024.
/// Phase 2 ships persistence, primitives, and reference-data seeding;
/// per-user-story slice handlers, workers, subscribers, and storefront
/// engine land in subsequent phases per
/// <c>specs/phase-1D/024-cms/tasks.md</c>.
/// </summary>
public static class CmsModule
{
    public static IServiceCollection AddCmsModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var connectionString = configuration.ResolveRequiredDefaultConnectionString(hostEnvironment);

        services.AddDbContext<CmsDbContext>((provider, options) =>
        {
            var dataSource = provider.GetService<NpgsqlDataSource>();
            if (dataSource is not null)
            {
                options.UseNpgsql(dataSource);
            }
            else
            {
                options.UseNpgsql(connectionString);
            }
            // ManyServiceProvidersCreatedWarning suppressed (project-memory rule).
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        });

        services.AddDbContextFactory<CmsDbContext>((provider, options) =>
        {
            var dataSource = provider.GetService<NpgsqlDataSource>();
            if (dataSource is not null)
            {
                options.UseNpgsql(dataSource);
            }
            else
            {
                options.UseNpgsql(connectionString);
            }
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        }, lifetime: ServiceLifetime.Singleton);

        // Primitives — pure helpers reused across slices.
        services.AddSingleton<BannerCapacityCalculator>();
        services.AddSingleton<Storefront.StorefrontContentResolver>();

        // US1 — banner authoring + lifecycle slices.
        services.AddScoped<Editor.SaveBannerDraft.SaveBannerDraftHandler>();
        services.AddScoped<Publisher.SchedulePublish.SchedulePublishBannerHandler>();
        services.AddScoped<Publisher.PublishNow.PublishNowBannerHandler>();
        services.AddScoped<Publisher.ArchiveContent.ArchiveBannerHandler>();
        services.AddScoped<BannerCtaValidator>();

        // US2 — storefront read slices.
        services.AddScoped<Storefront.ListBannerSlots.ListBannerSlotsHandler>();
        services.AddScoped<Storefront.ListFeaturedSections.ListFeaturedSectionsHandler>();
        services.AddScoped<FeaturedSectionResolver>();
        services.AddCmsStorefrontRateLimit(configuration);

        // US3 — legal-page authoring + supersession.
        services.AddScoped<SaveLegalPageVersionDraftHandler>();
        services.AddScoped<SchedulePublishLegalPageVersionHandler>();
        services.AddScoped<PublishLegalPageVersionNowHandler>();
        services.AddScoped<ListLegalPageVersionHistoryHandler>();
        services.AddScoped<GetLegalPageHandler>();
        services.AddSingleton<LegalPageSupersessionTransaction>();

        // US4 — blog article authoring + preview tokens.
        services.AddScoped<SaveBlogArticleDraftHandler>();
        services.AddScoped<PublishNowBlogArticleHandler>();
        services.AddScoped<ListBlogArticlesHandler>();
        services.AddScoped<GetBlogArticleHandler>();
        services.AddScoped<MintPreviewTokenHandler>();
        services.AddScoped<RevokePreviewTokenHandler>();
        services.AddScoped<ReadPreviewedDraftHandler>();
        services.AddSingleton(provider =>
        {
            var key = configuration["Cms:Preview:HmacKey"];
            byte[] bytes;
            if (!string.IsNullOrWhiteSpace(key))
            {
                try { bytes = Convert.FromBase64String(key); }
                catch (FormatException) { bytes = System.Text.Encoding.UTF8.GetBytes(key); }
            }
            else
            {
                // Dev/test default — production wires Cms:Preview:HmacKey through Key Vault.
                bytes = System.Text.Encoding.UTF8.GetBytes(
                    "cms-preview-dev-key-do-not-use-in-production-32bytes!!");
            }
            if (bytes.Length < 32)
            {
                Array.Resize(ref bytes, 32);
            }
            return new BackendApi.Modules.Cms.Primitives.PreviewTokenSigner(bytes);
        });
        services.AddHostedService<Workers.CmsPreviewTokenCleanupWorker>();

        // US5/US6 — featured-section + FAQ authoring + shared publish slice.
        services.AddScoped<SaveFeaturedSectionDraftHandler>();
        services.AddScoped<SaveFaqEntryDraftHandler>();
        services.AddScoped<BulkReorderFaqEntriesHandler>();
        services.AddScoped<Storefront.ListFaqEntries.ListFaqEntriesHandler>();
        services.AddScoped<Publisher.PublishContentSlice>();

        // Phase 9 — dev seeder.
        services.AddScoped<ISeeder, CmsV1DevSeeder>();

        // Phase 10 — workers + subscribers + outbound binding publisher.
        services.AddHostedService<Workers.CmsAssetGarbageCollectorWorker>();
        services.AddHostedService<Workers.CmsStaleDraftAlertWorker>();
        services.AddScoped<INotificationHandler<PricingCampaignDeactivated>, CampaignDeactivatedHandler>();
        services.AddScoped<ICmsCampaignBindingPublisher, CmsCampaignBindingPublisher>();

        // Cross-module catalog read contract fallbacks. TryAdd lets spec 005
        // supply production implementations without coordinating registration
        // order. Until 005 ships, the in-process fakes (Modules/Shared/Testing)
        // are wired in tests; production runs against a Null implementation
        // declared below to fail-fast at the publish gate.
        services.TryAddScoped<ICatalogProductReadContract, NullCatalogProductReadContract>();
        services.TryAddScoped<ICatalogCategoryReadContract, NullCatalogCategoryReadContract>();
        services.TryAddScoped<ICatalogBundleReadContract, NullCatalogBundleReadContract>();

        // MediatR — scan our assembly so the IPublisher in handlers can
        // dispatch CMS domain events. AddMediatR is idempotent across modules.
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<CmsBannerPublished>());

        // US1 — scheduled publish worker.
        services.AddHostedService<Workers.CmsScheduledPublishWorker>();

        // Reference-data seeding (idempotent across Dev / Staging / Production).
        services.AddScoped<ISeeder, CmsReferenceDataSeeder>();

        services.AddSingleton(TimeProvider.System);
        return services;
    }

    /// <summary>
    /// Overload preserved for the existing <c>Program.cs</c> wiring; resolves
    /// the host environment from configuration so the connection-string lookup
    /// still works.
    /// </summary>
    public static IServiceCollection AddCmsModule(
        this IServiceCollection services,
        IConfiguration configuration)
        => services.AddCmsModule(configuration, new ImplicitHostEnvironment(configuration));

    /// <summary>
    /// Maps the V1 admin CMS endpoints (US1 banner authoring + lifecycle).
    /// Subsequent user stories add their own endpoint extensions hung off this
    /// same <c>/v1/admin/cms</c> route group.
    /// </summary>
    public static Microsoft.AspNetCore.Routing.IEndpointRouteBuilder MapCmsAdminEndpoints(
        this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/v1/admin/cms");
        admin.MapSaveBannerDraftEndpoints();
        admin.MapPublisherBannerEndpoints();
        admin.MapLegalOwnerEndpoints();
        admin.MapSaveBlogArticleDraftEndpoints();
        admin.MapPublishNowBlogArticleEndpoints();
        admin.MapPreviewAdminEndpoints();
        admin.MapSaveFeaturedSectionDraftEndpoints();
        admin.MapSaveFaqEntryDraftEndpoints();
        admin.MapBulkReorderFaqEntriesEndpoint();
        admin.MapPublishContentEndpoints();
        admin.MapDraftManagementEndpoints();
        admin.MapSuperAdminEndpoints();
        return endpoints;
    }

    /// <summary>
    /// Maps the V1 storefront CMS endpoints (US2 banner + featured-section
    /// reads). All endpoints are <c>[AllowAnonymous]</c> per Principle 3 and
    /// rate-limited per IP via the <c>cms-storefront</c> policy.
    /// </summary>
    public static Microsoft.AspNetCore.Routing.IEndpointRouteBuilder MapCmsStorefrontEndpoints(
        this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints)
    {
        var storefront = endpoints.MapGroup("/v1/storefront/cms");
        storefront.MapListBannerSlotsEndpoint();
        storefront.MapListFeaturedSectionsEndpoint();
        storefront.MapGetLegalPageEndpoint();
        storefront.MapListBlogArticlesEndpoint();
        storefront.MapGetBlogArticleEndpoint();
        storefront.MapPreviewStorefrontEndpoint();
        storefront.MapListFaqEntriesEndpoint();
        return endpoints;
    }

    /// <summary>
    /// Production-safe Null catalog read fallback. Returns
    /// <see cref="LinkedEntityUnavailableReason.NotFound"/> so the publish
    /// gate hard-fails until spec 005 ships a real implementation.
    /// Tests bind <c>FakeCatalog*ReadContract</c> instead.
    /// </summary>
    internal sealed class NullCatalogProductReadContract : ICatalogProductReadContract
    {
        public Task<CatalogProductRead> ReadAsync(Guid productId, string marketCode, CancellationToken ct) =>
            Task.FromResult(new CatalogProductRead(
                ProductId: productId,
                MarketCode: marketCode,
                DisplayNameAr: string.Empty,
                DisplayNameEn: string.Empty,
                VendorId: null,
                IsAvailable: false,
                UnavailableReason: LinkedEntityUnavailableReason.NotFound));
    }

    internal sealed class NullCatalogCategoryReadContract : ICatalogCategoryReadContract
    {
        public Task<CatalogCategoryRead> ReadAsync(Guid categoryId, string marketCode, CancellationToken ct) =>
            Task.FromResult(new CatalogCategoryRead(
                CategoryId: categoryId,
                MarketCode: marketCode,
                DisplayNameAr: string.Empty,
                DisplayNameEn: string.Empty,
                IsAvailable: false,
                UnavailableReason: LinkedEntityUnavailableReason.NotFound));
    }

    internal sealed class NullCatalogBundleReadContract : ICatalogBundleReadContract
    {
        public Task<CatalogBundleRead> ReadAsync(Guid bundleId, string marketCode, CancellationToken ct) =>
            Task.FromResult(new CatalogBundleRead(
                BundleId: bundleId,
                MarketCode: marketCode,
                DisplayNameAr: string.Empty,
                DisplayNameEn: string.Empty,
                IsAvailable: false,
                UnavailableReason: LinkedEntityUnavailableReason.NotFound));
    }

    private sealed class ImplicitHostEnvironment(IConfiguration configuration) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } =
            configuration["DOTNET_ENVIRONMENT"]
            ?? configuration["ASPNETCORE_ENVIRONMENT"]
            ?? Environments.Production;
        public string ApplicationName { get; set; } = "backend_api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
