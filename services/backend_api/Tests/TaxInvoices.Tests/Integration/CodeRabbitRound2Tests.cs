using System.Net.Http.Json;
using BackendApi.Modules.TaxInvoices.Entities;
using BackendApi.Modules.TaxInvoices.Internal.IssueCreditNote;
using BackendApi.Modules.TaxInvoices.Internal.IssueOnCapture;
using BackendApi.Modules.TaxInvoices.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxInvoices.Tests.Infrastructure;

namespace TaxInvoices.Tests.Integration;

/// <summary>
/// Regressions for CodeRabbit PR #33 round-2 critical / major findings.
/// </summary>
[Collection("invoices-fixture")]
public sealed class CodeRabbitRound2Tests(InvoicesTestFactory factory)
{
    /// <summary>
    /// R2 Major (rounding remainder) — three partial qty-1 credits against a Qty=3 line
    /// with LineDiscountMinor=1 must reconcile. Earlier code re-rounded each credit's
    /// pro-rated discount and produced 0+0+0; the final credit now picks up the leftover
    /// minor unit and the cumulative-vs-original check stays consistent.
    /// </summary>
    [Fact]
    public async Task R2Rounding_ThreePartialCredits_ReconcileToOriginalDiscount()
    {
        await factory.ResetDatabaseAsync();
        var accountId = await InvoicesTestSeed.SeedAccountAsync(factory);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var ordersDb = scope.ServiceProvider.GetRequiredService<BackendApi.Modules.Orders.Persistence.OrdersDbContext>();
            var order = new BackendApi.Modules.Orders.Entities.Order
            {
                Id = Guid.NewGuid(), OrderNumber = "ORD-KSA-202604-ROUND",
                AccountId = accountId, MarketCode = "KSA", Currency = "SAR",
                SubtotalMinor = 300_00, DiscountMinor = 1, TaxMinor = 0, GrandTotalMinor = 299_99,
                PriceExplanationId = Guid.NewGuid(),
                ShippingAddressJson = "{}", BillingAddressJson = "{}",
                OrderState = "placed", PaymentState = "captured",
                FulfillmentState = "not_started", RefundState = "none",
                PlacedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            order.Lines.Add(new BackendApi.Modules.Orders.Entities.OrderLine
            {
                Id = Guid.NewGuid(), OrderId = order.Id, ProductId = Guid.NewGuid(),
                Sku = "ROUND", NameAr = "ت", NameEn = "Round",
                Qty = 3, UnitPriceMinor = 100_00, LineDiscountMinor = 1, LineTaxMinor = 0,
                LineTotalMinor = 299_99, AttributesJson = "{}",
            });
            ordersDb.Orders.Add(order);
            await ordersDb.SaveChangesAsync();
            (await scope.ServiceProvider.GetRequiredService<IssueOnCaptureHandler>()
                .IssueAsync(order.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();
        }

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
        var invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines).SingleAsync();
        var lineId = invoice.Lines.Single().Id;
        var creditHandler = verifyScope.ServiceProvider.GetRequiredService<IssueCreditNoteHandler>();

        // Three partial qty-1 credits — must each succeed and the cumulative discount sum
        // must equal exactly LineDiscountMinor=1.
        var first = await creditHandler.IssueAsync(new IssueCreditNoteRequest(
            invoice.Id, Guid.NewGuid(),
            new[] { new CreditNoteLineInput(lineId, 1) }, "customer_return"),
            CancellationToken.None);
        first.IsSuccess.Should().BeTrue();
        var second = await creditHandler.IssueAsync(new IssueCreditNoteRequest(
            invoice.Id, Guid.NewGuid(),
            new[] { new CreditNoteLineInput(lineId, 1) }, "customer_return"),
            CancellationToken.None);
        second.IsSuccess.Should().BeTrue();
        var third = await creditHandler.IssueAsync(new IssueCreditNoteRequest(
            invoice.Id, Guid.NewGuid(),
            new[] { new CreditNoteLineInput(lineId, 1) }, "customer_return"),
            CancellationToken.None);
        third.IsSuccess.Should().BeTrue("the final credit must be allowed to reclaim the rounding remainder");

        var totalRefundedDiscount = await db.CreditNoteLines.AsNoTracking()
            .Where(cnl => cnl.InvoiceLineId == lineId)
            .SumAsync(cnl => cnl.LineDiscountMinor);
        totalRefundedDiscount.Should().Be(1, "the cumulative discount must reconcile to the original LineDiscountMinor");
    }

    /// <summary>
    /// R2 Major (RetryEndpoint atomicity + Attempts reset) — a job at MaxAttempts is
    /// retry-eligible only via this endpoint and the conditional UPDATE resets Attempts to 0.
    /// </summary>
    [Fact]
    public async Task R2_RetryEndpoint_ResetsAttemptsToZero_AndAtomicallyTransitions()
    {
        await factory.ResetDatabaseAsync();
        var accountId = await InvoicesTestSeed.SeedAccountAsync(factory);
        var order = await InvoicesTestSeed.SeedCapturedOrderAsync(factory, accountId);
        await using var scope = factory.Services.CreateAsyncScope();
        var issuer = scope.ServiceProvider.GetRequiredService<IssueOnCaptureHandler>();
        await issuer.IssueAsync(order.Id, CancellationToken.None);
        var db = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
        var job = await db.RenderJobs.SingleAsync();
        // Simulate an exhausted job.
        job.State = InvoiceRenderJob.StateFailed;
        job.Attempts = InvoiceRenderJob.MaxAttempts;
        await db.SaveChangesAsync();

        var (token, _) = await InvoicesAdminAuthHelperShim.IssueAsync(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var resp = await client.PostAsync($"/v1/admin/invoices/render-queue/{job.Id}/retry", null);
        resp.EnsureSuccessStatusCode();

        var after = await db.RenderJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id);
        after.State.Should().Be(InvoiceRenderJob.StateQueued);
        after.Attempts.Should().Be(0, "admin retry resets Attempts so the worker's MaxAttempts gate doesn't lock the job out");
    }

    /// <summary>R2 Major — verifies the `invoice.regenerate_queued` request event is emitted
    /// by the regenerate endpoint. The terminal `invoice.regenerated` event is asserted by
    /// the worker-rendering integration tests; here we cover the queue-time behaviour.</summary>
    [Fact]
    public async Task R2_RegenerateQueuedEvent_EmittedAtRequestTime()
    {
        await factory.ResetDatabaseAsync();
        var accountId = await InvoicesTestSeed.SeedAccountAsync(factory);
        var order = await InvoicesTestSeed.SeedCapturedOrderAsync(factory, accountId);
        await using var scope = factory.Services.CreateAsyncScope();
        var issuer = scope.ServiceProvider.GetRequiredService<IssueOnCaptureHandler>();
        await issuer.IssueAsync(order.Id, CancellationToken.None);
        var db = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
        var invoice = await db.Invoices.SingleAsync();
        // Simulate a prior render: bump RenderAttempts so the worker's emit-event branch
        // treats this as a regenerate.
        invoice.RenderAttempts = 1;
        invoice.PdfBlobKey = "prior-key";
        invoice.PdfSha256 = "deadbeef";
        await db.SaveChangesAsync();
        // The worker isn't running in Test env; we exercise its render path indirectly via
        // the regenerate endpoint which ALSO records the regenerate_queued outbox.
        // The terminal `invoice.regenerated` is asserted via the worker logic in the
        // Render integration tests; here we just confirm the request event ships.
        var (adminToken, _) = await InvoicesAdminAuthHelperShim.IssueAsync(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
        var resp = await client.PostAsJsonAsync(
            $"/v1/admin/invoices/{invoice.Id}/regenerate",
            new { reason = "operator-request" });
        resp.EnsureSuccessStatusCode();

        var queuedEvent = await db.Outbox.AsNoTracking()
            .FirstOrDefaultAsync(e => e.AggregateId == invoice.Id && e.EventType == "invoice.regenerate_queued");
        queuedEvent.Should().NotBeNull("the request-queued event must be emitted by the regenerate endpoint");
    }

    /// <summary>R2 Major — production startup must fail fast if no IInvoiceBlobStore is wired.
    /// We exercise the validator by registering the module against a synthetic Production
    /// hosting environment with no Azure adapter.</summary>
    [Fact]
    public void R2_ProductionMissingBlobStore_FailsAtStartup()
    {
        // The factory we already use registers LocalFs; assert the module's production guard
        // throws when the same registration is attempted with a Production host environment.
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        var configBuilder = new Microsoft.Extensions.Configuration.ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=irrelevant;",
        });
        var config = configBuilder.Build();
        var prodEnv = new ProdHostEnvironment();
        var act = () => BackendApi.Modules.TaxInvoices.TaxInvoicesModule.AddTaxInvoicesModule(services, config, prodEnv);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IInvoiceBlobStore*production*");
    }

    private sealed class ProdHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}

/// <summary>Tiny helper for R2 tests to issue an admin JWT.</summary>
internal static class InvoicesAdminAuthHelperShim
{
    public static async Task<(string Token, Guid AccountId)> IssueAsync(InvoicesTestFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BackendApi.Modules.Identity.Persistence.IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<BackendApi.Modules.Identity.Primitives.Argon2idHasher>();
        var authSessions = scope.ServiceProvider.GetRequiredService<BackendApi.Modules.Identity.Admin.Common.AdminAuthSessionService>();
        var now = DateTimeOffset.UtcNow;
        var account = new BackendApi.Modules.Identity.Entities.Account
        {
            Id = Guid.NewGuid(),
            Surface = "admin",
            MarketCode = "platform",
            EmailNormalized = $"inv-admin-{Guid.NewGuid():N}@x.test",
            EmailDisplay = $"inv-admin-{Guid.NewGuid():N}@x.test",
            PasswordHash = hasher.HashPassword("InvTests!123", BackendApi.Modules.Identity.Primitives.SurfaceKind.Admin),
            PasswordHashVersion = 1, PermissionVersion = 1, Status = "active",
            EmailVerifiedAt = now, Locale = "en", DisplayName = "Inv Admin",
            CreatedAt = now, UpdatedAt = now,
        };
        db.Accounts.Add(account);
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Code == "invoices.admin");
        if (role is null)
        {
            role = new BackendApi.Modules.Identity.Entities.Role
            {
                Id = Guid.NewGuid(), Code = "invoices.admin",
                NameAr = "x", NameEn = "x", Scope = "platform", System = true,
            };
            db.Roles.Add(role);
        }
        foreach (var perm in new[] { "invoices.read", "invoices.regenerate", "invoices.resend", "invoices.finance.export", "invoices.credit_note.issue", "invoices.issue_on_capture" })
        {
            var p = await db.Permissions.SingleOrDefaultAsync(x => x.Code == perm);
            if (p is null)
            {
                p = new BackendApi.Modules.Identity.Entities.Permission { Id = Guid.NewGuid(), Code = perm, Description = perm };
                db.Permissions.Add(p);
            }
            if (!await db.RolePermissions.AnyAsync(rp => rp.RoleId == role.Id && rp.PermissionId == p.Id))
            {
                db.RolePermissions.Add(new BackendApi.Modules.Identity.Entities.RolePermission { RoleId = role.Id, PermissionId = p.Id });
            }
        }
        db.AccountRoles.Add(new BackendApi.Modules.Identity.Entities.AccountRole
        {
            AccountId = account.Id, RoleId = role.Id, MarketCode = "platform", GrantedAt = now,
        });
        await db.SaveChangesAsync();
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Request.Headers.UserAgent = "inv-tests";
        var session = await authSessions.IssueAdminSessionAsync(account, http, CancellationToken.None);
        return (session.AccessToken, account.Id);
    }
}
