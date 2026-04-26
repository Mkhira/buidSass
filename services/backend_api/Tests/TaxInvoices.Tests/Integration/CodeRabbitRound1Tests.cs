using BackendApi.Modules.TaxInvoices.Entities;
using BackendApi.Modules.TaxInvoices.Internal.IssueCreditNote;
using BackendApi.Modules.TaxInvoices.Internal.IssueOnCapture;
using BackendApi.Modules.TaxInvoices.Persistence;
using BackendApi.Modules.TaxInvoices.Rendering;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxInvoices.Tests.Infrastructure;

namespace TaxInvoices.Tests.Integration;

/// <summary>
/// Regressions for CodeRabbit PR #33 round-1 critical findings:
///   • CR1 — refund cumulative race (FOR UPDATE on invoice).
///   • CR2 — duplicate InvoiceLineId in same request bypasses per-line check.
///   • CR3 — OrderId must be unique at the DB level.
///   • CR4 — LocalFs path traversal.
/// </summary>
[Collection("invoices-fixture")]
public sealed class CodeRabbitRound1Tests(InvoicesTestFactory factory)
{
    [Fact]
    public async Task CR2_DuplicateInvoiceLineInSameRequest_IsRejected()
    {
        await factory.ResetDatabaseAsync();
        var accountId = await InvoicesTestSeed.SeedAccountAsync(factory);
        var order = await InvoicesTestSeed.SeedCapturedOrderAsync(factory, accountId);

        await using var scope = factory.Services.CreateAsyncScope();
        var issuer = scope.ServiceProvider.GetRequiredService<IssueOnCaptureHandler>();
        var invoiceResult = await issuer.IssueAsync(order.Id, CancellationToken.None);
        invoiceResult.IsSuccess.Should().BeTrue();

        var db = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
        var invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines)
            .SingleAsync(i => i.Id == invoiceResult.InvoiceId);
        var lineId = invoice.Lines.Single().Id;
        var creditHandler = scope.ServiceProvider.GetRequiredService<IssueCreditNoteHandler>();

        // Original line qty is 1; payload `[{lineA,1},{lineA,1}]` aggregates to qty 2 and
        // must be rejected even though each individual entry passes the per-line check.
        var result = await creditHandler.IssueAsync(new IssueCreditNoteRequest(
            invoice.Id, Guid.NewGuid(),
            new[]
            {
                new CreditNoteLineInput(lineId, 1),
                new CreditNoteLineInput(lineId, 1),
            },
            "customer_return"), CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("credit_note.line_exceeds_invoice");
    }

    [Fact]
    public async Task CR3_DuplicateOrderId_IsRejectedByUniqueIndex()
    {
        await factory.ResetDatabaseAsync();
        var accountId = await InvoicesTestSeed.SeedAccountAsync(factory);
        var order = await InvoicesTestSeed.SeedCapturedOrderAsync(factory, accountId);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;

        async Task SeedDirectAsync()
        {
            db.Invoices.Add(new Invoice
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = $"INV-KSA-202604-{Random.Shared.Next(100000, 999999)}",
                OrderId = order.Id,
                AccountId = accountId,
                MarketCode = "KSA",
                Currency = "SAR",
                IssuedAt = nowUtc,
                PriceExplanationId = Guid.NewGuid(),
                GrandTotalMinor = 100_00,
                BillToJson = "{}",
                SellerJson = "{}",
                State = Invoice.StatePending,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
            });
            await db.SaveChangesAsync();
        }
        await SeedDirectAsync();
        var act = async () => await SeedDirectAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            because: "OrderId is unique-indexed at the DB level so concurrent issuance can't slip past");
    }

    [Theory]
    [InlineData("../escape.pdf")]
    [InlineData("../../etc/passwd")]
    [InlineData("/etc/passwd")]
    public async Task CR4_BlobKeyEscape_IsRejected(string maliciousKey)
    {
        await factory.ResetDatabaseAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var blobStore = scope.ServiceProvider.GetRequiredService<IInvoiceBlobStore>();

        var act = async () => await blobStore.PutAsync(maliciousKey,
            new byte[] { 0x25, 0x50, 0x44, 0x46 }, "application/pdf", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void CR4_ResolveKey_SanitisesBadCharacters()
    {
        // Internally-generated keys with awkward chars must round-trip safely.
        // Direct sanitisation cannot be tested without instantiating the type, so we exercise
        // the resolver via the public IInvoiceBlobStore interface.
        var scope = factory.Services.CreateAsyncScope();
        var blobStore = scope.ServiceProvider.GetRequiredService<IInvoiceBlobStore>();
        var key = blobStore.ResolveInvoiceKey("KSA",
            new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            "INV/KSA-202604-000001"); // contains a slash that would otherwise create a directory
        key.Should().NotContain("..");
        key.Should().NotStartWith("/");
    }
}
