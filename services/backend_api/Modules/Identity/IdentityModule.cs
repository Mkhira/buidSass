using BackendApi.Features.Seeding;
using BackendApi.Modules.Identity.Authorization;
using BackendApi.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using BackendApi.Modules.Identity.Seeding;
using BackendApi.Modules.Identity.Customer.Register;
using BackendApi.Modules.Identity.Customer.ConfirmEmail;
using BackendApi.Modules.Identity.Customer.RequestOtp;
using BackendApi.Modules.Identity.Customer.VerifyOtp;
using BackendApi.Modules.Identity.Customer.SignIn;
using BackendApi.Modules.Identity.Customer.RefreshSession;
using BackendApi.Modules.Identity.Customer.SignOut;
using BackendApi.Modules.Identity.Customer.RequestPasswordReset;
using BackendApi.Modules.Identity.Customer.CompletePasswordReset;
using BackendApi.Modules.Identity.Customer.ChangePassword;
using BackendApi.Modules.Identity.Customer.ListSessions;
using BackendApi.Modules.Identity.Customer.RevokeSession;
using BackendApi.Modules.Identity.Customer.Me;
using BackendApi.Modules.Identity.Customer.SetLocale;
using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Admin.AcceptInvitation;
using BackendApi.Modules.Identity.Admin.EnrollTotp;
using BackendApi.Modules.Identity.Admin.ConfirmTotp;
using BackendApi.Modules.Identity.Admin.SignIn;
using BackendApi.Modules.Identity.Admin.CompleteMfaChallenge;
using BackendApi.Modules.Identity.Admin.RotateTotp;
using BackendApi.Modules.Identity.Admin.ResetAdminMfa;
using BackendApi.Modules.Identity.Admin.StepUpOtp;
using BackendApi.Modules.Identity.Admin.CompleteStepUpOtp;
using BackendApi.Modules.Identity.Admin.ListAdminSessions;
using BackendApi.Modules.Identity.Admin.RevokeAdminSession;
using BackendApi.Modules.Identity.Admin.InviteAdmin;
using BackendApi.Modules.Identity.Admin.RevokeInvitation;
using BackendApi.Modules.Identity.Admin.ChangeAdminRole;
using BackendApi.Modules.Identity.Admin.ListAdminMfaFactors;
using BackendApi.Modules.Identity.Admin.Me;

namespace BackendApi.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var connectionString = configuration.ResolveRequiredDefaultConnectionString(hostEnvironment);

        services.AddSingleton<IAdminMarketChangeContext, AdminMarketChangeContext>();
        services.AddSingleton(sp => new NpgsqlDataSourceBuilder(connectionString).Build());
        services.AddScoped<IdentitySaveChangesInterceptor>();
        services.AddDbContext<IdentityDbContext>((provider, options) =>
        {
            options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>());
            options.AddInterceptors(provider.GetRequiredService<IdentitySaveChangesInterceptor>());
        });
        services.Configure<IdentityJwtOptions>(configuration.GetSection(IdentityJwtOptions.SectionName));
        services.AddSingleton<IValidateOptions<IdentityJwtOptions>, IdentityJwtOptionsValidator>();
        services.Configure<IdentityMfaOptions>(configuration.GetSection(IdentityMfaOptions.SectionName));
        services.AddSingleton<Argon2idHasher>();
        services.AddSingleton<BreachListChecker>();
        services.AddSingleton<PhoneNormalizer>();
        services.AddSingleton<IdentityTokenSecretHasher>();
        services.AddSingleton<IdentityClientFingerprintHasher>();
        services.AddSingleton<IdentityClientSecurityHasher>();
        services.AddSingleton<IdentityTokenSigningProvider>();
        services.AddSingleton<IIdentityTokenSigningProvider>(provider => provider.GetRequiredService<IdentityTokenSigningProvider>());
        services.AddHostedService(provider => provider.GetRequiredService<IdentityTokenSigningProvider>());
        services.AddSingleton<IJwtIssuer, JwtIssuer>();
        services.AddSingleton<RefreshTokenRevocationStore>();
        services.AddSingleton<ITokenRevocationCache>(provider => provider.GetRequiredService<RefreshTokenRevocationStore>());
        services.AddSingleton<IRefreshTokenRevocationStore>(provider => provider.GetRequiredService<RefreshTokenRevocationStore>());
        services.AddHostedService<RefreshRevocationCacheWorker>();
        services.AddHostedService<IdentityMaintenancePurgeWorker>();
        services.AddScoped<PolicyEvaluator>();
        services.AddScoped<IAuthorizationAuditEmitter, AuthorizationAuditEmitter>();
        services.AddScoped<IRateLimitAuditSink, RateLimitAuditSink>();
        services.AddScoped<ISeeder, IdentityReferenceDataSeeder>();
        services.AddScoped<ISeeder, IdentityDevDataSeeder>();
        ConfigureDataProtection(services, configuration, hostEnvironment);
        services.AddMemoryCache();
        services.AddScoped<AdminPartialAuthTokenStore>();
        services.AddScoped<AdminMfaChallengeStore>();
        services.AddScoped<AdminAuthSessionService>();
        services.AddScoped<CustomerAuthSessionService>();

        services
            .AddAuthentication()
            .AddJwtBearer("CustomerJwt", _ => { })
            .AddJwtBearer("AdminJwt", _ => { });

        services.AddOptions<JwtBearerOptions>("CustomerJwt")
            .Configure<IIdentityTokenSigningProvider>((options, keyProvider) =>
                ConfigureJwtBearer(options, keyProvider.GetSurface(SurfaceKind.Customer)));

        services.AddOptions<JwtBearerOptions>("AdminJwt")
            .Configure<IIdentityTokenSigningProvider>((options, keyProvider) =>
                ConfigureJwtBearer(options, keyProvider.GetSurface(SurfaceKind.Admin)));

        services.AddAuthorization();

        services.AddRateLimiter(options => RateLimitPolicies.RegisterAll(options, configuration));

        services.AddScoped<IOtpChallengeDispatcher>(provider =>
        {
            var env = provider.GetRequiredService<IHostEnvironment>();
            if (env.IsDevelopment())
            {
                return new ConsoleOtpDispatcher(provider.GetRequiredService<ILogger<ConsoleOtpDispatcher>>());
            }

            return new NotConfiguredOtpDispatcher();
        });

        services.AddScoped<IIdentityEmailDispatcher>(provider =>
        {
            var env = provider.GetRequiredService<IHostEnvironment>();
            if (env.IsDevelopment())
            {
                return new ConsoleEmailDispatcher(provider.GetRequiredService<ILogger<ConsoleEmailDispatcher>>());
            }

            return new NotConfiguredEmailDispatcher();
        });

        return services;
    }

    public static WebApplication UseIdentityModuleEndpoints(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<IdentityRateLimitPartitionMiddleware>();
        app.UseRateLimiter();

        var customerIdentity = app.MapGroup("/v1/customer/identity");
        customerIdentity.MapGet("/.well-known/jwks.json", (IIdentityTokenSigningProvider keyProvider) =>
            Results.Json(keyProvider.BuildJwks(SurfaceKind.Customer)));
        customerIdentity.MapRegisterEndpoint();
        customerIdentity.MapConfirmEmailEndpoint();
        customerIdentity.MapRequestOtpEndpoint();
        customerIdentity.MapVerifyOtpEndpoint();
        customerIdentity.MapCustomerSignInEndpoint();
        customerIdentity.MapRefreshSessionEndpoint();
        customerIdentity.MapSignOutEndpoint();
        customerIdentity.MapRequestPasswordResetEndpoint();
        customerIdentity.MapCompletePasswordResetEndpoint();
        customerIdentity.MapChangePasswordEndpoint();
        customerIdentity.MapListSessionsEndpoint();
        customerIdentity.MapRevokeSessionEndpoint();
        customerIdentity.MapCustomerMeEndpoint();
        customerIdentity.MapSetLocaleEndpoint();
        customerIdentity.MapCustomerTestProtectedEndpoint();

        var adminIdentity = app.MapGroup("/v1/admin/identity");
        adminIdentity.MapGet("/.well-known/jwks.json", (IIdentityTokenSigningProvider keyProvider) =>
            Results.Json(keyProvider.BuildJwks(SurfaceKind.Admin)));
        adminIdentity.MapAcceptInvitationEndpoint();
        adminIdentity.MapEnrollTotpEndpoint();
        adminIdentity.MapConfirmTotpEndpoint();
        adminIdentity.MapAdminSignInEndpoint();
        adminIdentity.MapCompleteMfaChallengeEndpoint();
        adminIdentity.MapStepUpOtpEndpoint();
        adminIdentity.MapCompleteStepUpOtpEndpoint();
        adminIdentity.MapRotateTotpEndpoint();
        adminIdentity.MapResetAdminMfaEndpoint();
        adminIdentity.MapListAdminSessionsEndpoint();
        adminIdentity.MapRevokeAdminSessionEndpoint();
        adminIdentity.MapInviteAdminEndpoint();
        adminIdentity.MapRevokeInvitationEndpoint();
        adminIdentity.MapChangeAdminRoleEndpoint();
        adminIdentity.MapListAdminMfaFactorsEndpoint();
        adminIdentity.MapAdminMeEndpoint();
        adminIdentity.MapAdminTestProtectedEndpoint();
        adminIdentity.MapAdminStepUpProtectedEndpoint();

        return app;
    }

    private static void ConfigureJwtBearer(
        JwtBearerOptions options,
        TokenSigningSurface signingSurface)
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidIssuer = signingSurface.Issuer,
            ValidAudience = signingSurface.Audience,
            IssuerSigningKey = signingSurface.SigningKey,
            IssuerSigningKeys = signingSurface.ValidationKeys,
        };
    }

    private const string DataProtectionApplicationName = "BackendApi.Identity";
    private const string DefaultDevKeyRingRelativePath = "infra/dev-keys/dataprotection";

    private static void ConfigureDataProtection(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var builder = services.AddDataProtection()
            .SetApplicationName(DataProtectionApplicationName);

        var configuredPath = configuration["Identity:DataProtection:KeyRingPath"];
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var absolute = Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.GetFullPath(Path.Combine(hostEnvironment.ContentRootPath ?? Directory.GetCurrentDirectory(), configuredPath));
            Directory.CreateDirectory(absolute);
            builder.PersistKeysToFileSystem(new DirectoryInfo(absolute));
            return;
        }

        if (hostEnvironment.IsDevelopment() || hostEnvironment.IsEnvironment("Test"))
        {
            var contentRoot = string.IsNullOrWhiteSpace(hostEnvironment.ContentRootPath)
                ? Directory.GetCurrentDirectory()
                : hostEnvironment.ContentRootPath;
            var fallback = Path.GetFullPath(Path.Combine(contentRoot, DefaultDevKeyRingRelativePath));
            Directory.CreateDirectory(fallback);
            builder.PersistKeysToFileSystem(new DirectoryInfo(fallback));
            return;
        }

        throw new InvalidOperationException(
            "Identity:DataProtection:KeyRingPath is required for Staging and Production (set it to a durable, shared-across-instances filesystem path or switch to Azure Blob/KeyVault providers).");
    }
}
