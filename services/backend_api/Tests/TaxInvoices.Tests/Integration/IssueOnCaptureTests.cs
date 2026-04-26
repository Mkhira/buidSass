using BackendApi.Modules.TaxInvoices.Entities;
using BackendApi.Modules.TaxInvoices.Internal.IssueOnCapture;
using BackendApi.Modules.TaxInvoices.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxInvoices.Tests.Infrastructure;

namespace TaxInvoices.Tests.Integration;

/// <summary>H3 / SC-001 — payment.captured triggers invoice issuance.</summary>
[Collection("invoices-fixture")]
public sealed class IssueOnCaptureTests(InvoicesTestFactory factory)
{
    [Fact]
    public async Task IssueAsync_OnCapturedOrder_PersistsInvoiceAndQueuesRender()
    {
        await factory.ResetDatabaseAsync();
        var accountId = await InvoicesTestSeed.SeedAccountAsync(factory);
        var order = await InvoicesTestSeed.SeedCapturedOrderAsync(factory, accountId, market: "KSA");

        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IssueOnCaptureHandler>();
        var result = await handler.IssueAsync(order.Id, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        result.InvoiceNumber.Should().MatchRegex("^INV-KSA-\\d{6}-\\d{6}$");

        var db = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
        var invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines)
            .SingleAsync(i => i.Id == result.InvoiceId);
        invoice.OrderId.Should().Be(order.Id);
        invoice.AccountId.Should().Be(accountId);
        invoice.Currency.Should().Be("SAR");
        invoice.GrandTotalMinor.Should().Be(order.GrandTotalMinor);
        invoice.State.Should().Be(Invoice.StatePending);
        invoice.ZatcaQrB64.Should().NotBeNullOrEmpty();    // KSA → QR populated.
        invoice.Lines.Should().HaveCount(1);
        invoice.Lines.Single().TaxRateBp.Should().Be(1500);

        var jobCount = await db.RenderJobs.AsNoTracking()
            .CountAsync(j => j.InvoiceId == invoice.Id);
        jobCount.Should().Be(1);
        var outboxCount = await db.Outbox.AsNoTracking()
            .CountAsync(e => e.AggregateId == invoice.Id && e.EventType == "invoice.issued");
        outboxCount.Should().Be(1);
    }

    [Fact]
    public async Task IssueAsync_IsIdempotentOnRetry()
    {
        await factory.ResetDatabaseAsync();
        var accountId = await InvoicesTestSeed.SeedAccountAsync(factory);
        var order = await InvoicesTestSeed.SeedCapturedOrderAsync(factory, accountId);

        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IssueOnCaptureHandler>();
        var first = await handler.IssueAsync(order.Id, CancellationToken.None);
        var second = await handler.IssueAsync(order.Id, CancellationToken.None);
        first.InvoiceId.Should().Be(second.InvoiceId);
        first.InvoiceNumber.Should().Be(second.InvoiceNumber);

        var db = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
        (await db.Invoices.CountAsync(i => i.OrderId == order.Id)).Should().Be(1);
    }

    [Fact]
    public async Task IssueAsync_OnEgMarket_SkipsZatcaQr()
    {
        await factory.ResetDatabaseAsync();
        var accountId = await InvoicesTestSeed.SeedAccountAsync(factory, market: "eg");
        var order = await InvoicesTestSeed.SeedCapturedOrderAsync(factory, accountId, market: "EG");

        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IssueOnCaptureHandler>();
        var result = await handler.IssueAsync(order.Id, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        var db = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
        var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == result.InvoiceId);
        invoice.Currency.Should().Be("EGP");
        invoice.ZatcaQrB64.Should().BeNull();              // EG → no QR (R12).
    }
}
