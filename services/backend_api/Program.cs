using BackendApi.Configuration;
using BackendApi.Features.Seeding;
using BackendApi.Modules.Catalog;
using BackendApi.Modules.Identity;
using BackendApi.Modules.Identity.Seeding;
using BackendApi.Modules.Observability;
using BackendApi.Modules.Pricing;
using BackendApi.Modules.Search;
using BackendApi.Modules.Inventory;
using BackendApi.Modules.B2B;
using BackendApi.Modules.Cart;
using BackendApi.Modules.Checkout;
using BackendApi.Modules.Orders;
using BackendApi.Modules.Returns;
using BackendApi.Modules.TaxInvoices;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Reviews;
using BackendApi.Modules.Cms;
using BackendApi.Modules.Verification;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Fast-path Production hard-block for the seed CLI: evaluate BEFORE AddLayeredConfiguration so
// that a missing / unreachable Key Vault cannot mask SeedGuard's intent. See A1 §5.2.
// Dry-run is allowed in all environments per A1 §5.4 (diagnostic-only, no writes).
if (args.Length > 0
    && string.Equals(args[0], SeedingCliVerb.Verb, StringComparison.Ordinal)
    && builder.Environment.IsProduction()
    && !args.Any(a => a.Equals("--mode=dry-run", StringComparison.Ordinal)))
{
    Console.Error.WriteLine(SeedGuard.ProductionBlockedMessage);
    return 1;
}

builder.AddLayeredConfiguration();

builder.Host.UseSerilog((_, _, loggerConfiguration) =>
{
    loggerConfiguration
        .Enrich.FromLogContext()
        .WriteTo.Console(new RenderedCompactJsonFormatter());
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Resolve eagerly so a missing connection string fails at startup with a clear message,
// matching the pre-existing behavior. The actual UseNpgsql call is deferred to scope time
// so test hosts that override IConfiguration via ConfigureAppConfiguration take effect —
// otherwise the variable would freeze before the factory's overrides apply.
_ = builder.Configuration.ResolveRequiredDefaultConnectionString(builder.Environment);

builder.Services.AddDbContext<AppDbContext>((provider, options) =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var hostEnvironment = provider.GetRequiredService<IHostEnvironment>();
    options.UseNpgsql(configuration.ResolveRequiredDefaultConnectionString(hostEnvironment));
});

// Spec 003 T011 module stubs (sequentially expanded by T043/T054/T064/T073).
builder.Services.AddAuditLogModule();
builder.Services.AddStorageModule();
builder.Services.AddPdfModule();
builder.Services.AddObservabilityModule();
builder.Services.AddIdentityModule(builder.Configuration, builder.Environment);
builder.Services.AddCatalogModule(builder.Configuration, builder.Environment);
builder.Services.AddSearchModule(builder.Configuration, builder.Environment);
builder.Services.AddPricingModule(builder.Configuration, builder.Environment);
builder.Services.AddInventoryModule(builder.Configuration, builder.Environment);
builder.Services.AddCartModule(builder.Configuration, builder.Environment);
builder.Services.AddCheckoutModule(builder.Configuration, builder.Environment);
builder.Services.AddOrdersModule(builder.Configuration, builder.Environment);
builder.Services.AddTaxInvoicesModule(builder.Configuration, builder.Environment);
builder.Services.AddReturnsModule(builder.Configuration, builder.Environment);
builder.Services.AddVerificationModule(builder.Configuration, builder.Environment);
builder.Services.AddReviewsModule(builder.Configuration, builder.Environment);
builder.Services.AddCmsModule(builder.Configuration);
builder.Services.AddB2BModule(builder.Configuration, builder.Environment);
builder.Services.AddSeeding(builder.Configuration);

// spec-024 R14 / spec-022 R-rate-limit — register forwarded-headers options so
// request.Connection.RemoteIpAddress reflects the true client IP behind a
// proxy / load balancer. Without this, the storefront rate-limit policies
// partition by the proxy IP and collapse to a single bucket. Trusted-proxy
// CIDRs are configured per environment in appsettings.{env}.json under
// "ForwardedHeaders:KnownNetworks" / "ForwardedHeaders:KnownProxies"; default
// behavior in Development clears the lists so any X-Forwarded-For value is
// honored locally. Production must set the trusted-proxy CIDRs explicitly.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    var configured = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? Array.Empty<string>();
    options.KnownProxies.Clear();
    foreach (var p in configured)
    {
        if (System.Net.IPAddress.TryParse(p, out var ip)) options.KnownProxies.Add(ip);
    }
    var networks = builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? Array.Empty<string>();
    options.KnownNetworks.Clear();
    foreach (var n in networks)
    {
        var parts = n.Split('/');
        if (parts.Length == 2 && System.Net.IPAddress.TryParse(parts[0], out var prefix) &&
            int.TryParse(parts[1], out var prefixLen))
        {
            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, prefixLen));
        }
    }
});

var app = builder.Build();

if (args.Length > 0 && string.Equals(args[0], SeedingCliVerb.Verb, StringComparison.Ordinal))
{
    return await SeedingCliVerb.RunAsync(app, args, CancellationToken.None);
}

if (args.Length > 0 && string.Equals(args[0], SeedAdminCliCommand.Verb, StringComparison.Ordinal))
{
    return await SeedAdminCliCommand.RunAsync(app, args, CancellationToken.None);
}

// Forwarded headers must run BEFORE rate-limiting / endpoint pipelines so
// downstream readers see the real client IP, not the proxy's.
app.UseForwardedHeaders();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseIdentityModuleEndpoints();
app.UseCatalogModuleEndpoints();
app.UseSearchModuleEndpoints();
app.UsePricingModuleEndpoints();
app.UseInventoryModuleEndpoints();
app.UseCartModuleEndpoints();
app.UseCheckoutModuleEndpoints();
app.UseOrdersModuleEndpoints();
app.UseTaxInvoicesModuleEndpoints();
app.UseReturnsModuleEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");
app.MapVerificationEndpoints();
app.MapReviewsEndpoints();
app.MapCmsAdminEndpoints();
app.MapCmsStorefrontEndpoints();
app.UseB2BModuleEndpoints();

await app.RunAsync();
return 0;

public partial class Program;
