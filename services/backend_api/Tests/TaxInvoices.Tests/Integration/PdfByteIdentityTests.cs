using System.Security.Cryptography;
using BackendApi.Modules.TaxInvoices.Rendering;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TaxInvoices.Tests.Infrastructure;

namespace TaxInvoices.Tests.Integration;

/// <summary>
/// SC-004 / H5 — PDF byte-identity invariant. Once a PDF is uploaded under an invoice's
/// deterministic blob key, every subsequent <c>GetAsync</c> returns the SAME bytes (and
/// SHA-256). This is the core contract behind the "stored PDF, never re-rendered on read"
/// promise (research R5). The render worker exercises the round-trip end-to-end; this test
/// proves the blob-store property in isolation across 100 repeats per the SC-004 envelope.
/// </summary>
[Collection("invoices-fixture")]
public sealed class PdfByteIdentityTests(InvoicesTestFactory factory)
{
    [Fact]
    public async Task BlobStore_RoundTrip_PreservesBytesAndSha()
    {
        await factory.ResetDatabaseAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var blobStore = scope.ServiceProvider.GetRequiredService<IInvoiceBlobStore>();

        var sample = new byte[2048];
        Random.Shared.NextBytes(sample);
        var expectedSha = Convert.ToHexString(SHA256.HashData(sample)).ToLowerInvariant();
        var key = blobStore.ResolveInvoiceKey("KSA",
            new DateTimeOffset(2026, 4, 15, 10, 0, 0, TimeSpan.Zero),
            "INV-KSA-202604-IDENTITY");

        await blobStore.PutAsync(key, sample, "application/pdf", CancellationToken.None);

        // 100/100 re-fetches must SHA-match (SC-004 envelope).
        for (var i = 0; i < 100; i++)
        {
            var fetched = await blobStore.GetAsync(key, CancellationToken.None);
            fetched.Should().NotBeNull();
            Convert.ToHexString(SHA256.HashData(fetched!)).ToLowerInvariant().Should().Be(expectedSha);
            fetched!.Should().Equal(sample);
        }
    }
}
