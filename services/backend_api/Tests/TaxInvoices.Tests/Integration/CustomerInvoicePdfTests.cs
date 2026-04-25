using System.Net;
using BackendApi.Modules.TaxInvoices.Entities;
using BackendApi.Modules.TaxInvoices.Internal.IssueOnCapture;
using BackendApi.Modules.TaxInvoices.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxInvoices.Tests.Infrastructure;

namespace TaxInvoices.Tests.Integration;

/// <summary>G2 — customer download path.</summary>
[Collection("invoices-fixture")]
public sealed class CustomerInvoicePdfTests(InvoicesTestFactory factory)
{
    [Fact]
    public async Task PendingInvoice_Returns409RenderPending_WithRetryAfter()
    {
        await factory.ResetDatabaseAsync();
        var (token, accountId) = await InvoicesCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await InvoicesTestSeed.SeedCapturedOrderAsync(factory, accountId);
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IssueOnCaptureHandler>();
        await handler.IssueAsync(order.Id, CancellationToken.None);
        // Invoice is in `pending` until the worker renders — the worker is gated off in Test env.

        var client = factory.CreateClient();
        InvoicesCustomerAuthHelper.SetBearer(client, token);
        var resp = await client.GetAsync($"/v1/customer/orders/{order.Id}/invoice.pdf");
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        resp.Headers.GetValues("Retry-After").Should().ContainSingle();
    }

    [Fact]
    public async Task CrossAccountAccess_Returns404()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountA) = await InvoicesCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var (tokenB, _) = await InvoicesCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await InvoicesTestSeed.SeedCapturedOrderAsync(factory, accountA);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IssueOnCaptureHandler>()
            .IssueAsync(order.Id, CancellationToken.None);
        var client = factory.CreateClient();
        InvoicesCustomerAuthHelper.SetBearer(client, tokenB);
        var resp = await client.GetAsync($"/v1/customer/orders/{order.Id}/invoice.pdf");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RenderedInvoice_StreamsStoredBytes()
    {
        await factory.ResetDatabaseAsync();
        var (token, accountId) = await InvoicesCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await InvoicesTestSeed.SeedCapturedOrderAsync(factory, accountId);
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IssueOnCaptureHandler>();
        var result = await handler.IssueAsync(order.Id, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        // Hand-flip the invoice to "rendered" + plant a fake blob so the customer endpoint
        // returns 200. End-to-end render-worker exercise is covered separately.
        var db = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
        var blobStore = scope.ServiceProvider.GetRequiredService<BackendApi.Modules.TaxInvoices.Rendering.IInvoiceBlobStore>();
        var invoice = await db.Invoices.SingleAsync(i => i.Id == result.InvoiceId);
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }; // "%PDF-"
        var key = blobStore.ResolveInvoiceKey(invoice.MarketCode, invoice.IssuedAt, invoice.InvoiceNumber);
        await blobStore.PutAsync(key, pdfBytes, "application/pdf", CancellationToken.None);
        invoice.PdfBlobKey = key;
        invoice.PdfSha256 = "deadbeef";
        invoice.State = Invoice.StateRendered;
        await db.SaveChangesAsync();

        var client = factory.CreateClient();
        InvoicesCustomerAuthHelper.SetBearer(client, token);
        var resp = await client.GetAsync($"/v1/customer/orders/{order.Id}/invoice.pdf");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        var body = await resp.Content.ReadAsByteArrayAsync();
        body.Should().StartWith(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D });
    }
}
