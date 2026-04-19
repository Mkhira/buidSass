using BackendApi.Modules.Observability;
using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");

app.Run();

public partial class Program;
