using BackendApi.Configuration;
using BackendApi.Features.Seeding;
using BackendApi.Modules.Observability;
using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Fast-path Production hard-block for the seed CLI: evaluate BEFORE AddLayeredConfiguration so
// that a missing / unreachable Key Vault cannot mask SeedGuard's intent. See A1 §5.2.
if (args.Length > 0
    && string.Equals(args[0], SeedingCliVerb.Verb, StringComparison.Ordinal)
    && builder.Environment.IsProduction())
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

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=dental_commerce;Username=dental_api_app;Password=dental_api_app";

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

// Spec 003 T011 module stubs (sequentially expanded by T043/T054/T064/T073).
builder.Services.AddAuditLogModule();
builder.Services.AddStorageModule();
builder.Services.AddPdfModule();
builder.Services.AddObservabilityModule();
builder.Services.AddSeeding(builder.Configuration);

var app = builder.Build();

if (args.Length > 0 && string.Equals(args[0], SeedingCliVerb.Verb, StringComparison.Ordinal))
{
    return await SeedingCliVerb.RunAsync(app, args, CancellationToken.None);
}

app.UseMiddleware<CorrelationIdMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");

await app.RunAsync();
return 0;

public partial class Program;
