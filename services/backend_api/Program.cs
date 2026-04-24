using BackendApi.Configuration;
using BackendApi.Features.Seeding;
using BackendApi.Modules.Catalog;
using BackendApi.Modules.Identity;
using BackendApi.Modules.Identity.Seeding;
using BackendApi.Modules.Observability;
using BackendApi.Modules.Pricing;
using BackendApi.Modules.Search;
using BackendApi.Modules.Inventory;
using BackendApi.Modules.Shared;
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

var connectionString = builder.Configuration.ResolveRequiredDefaultConnectionString(builder.Environment);

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

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
builder.Services.AddSeeding(builder.Configuration);

var app = builder.Build();

if (args.Length > 0 && string.Equals(args[0], SeedingCliVerb.Verb, StringComparison.Ordinal))
{
    return await SeedingCliVerb.RunAsync(app, args, CancellationToken.None);
}

if (args.Length > 0 && string.Equals(args[0], SeedAdminCliCommand.Verb, StringComparison.Ordinal))
{
    return await SeedAdminCliCommand.RunAsync(app, args, CancellationToken.None);
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseIdentityModuleEndpoints();
app.UseCatalogModuleEndpoints();
app.UseSearchModuleEndpoints();
app.UsePricingModuleEndpoints();
app.UseInventoryModuleEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");

await app.RunAsync();
return 0;

public partial class Program;
