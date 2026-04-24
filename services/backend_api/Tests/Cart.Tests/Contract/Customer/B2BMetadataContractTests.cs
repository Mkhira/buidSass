using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using Cart.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.Tests.Contract.Customer;

[Collection("cart-fixture")]
public sealed class B2BMetadataContractTests(CartTestFactory factory)
{
    [Fact]
    public async Task SetB2B_NonB2BAccount_Returns403()
    {
        await factory.ResetDatabaseAsync();
        var (accessToken, _) = await CartCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        var client = factory.CreateClient();
        CartCustomerAuthHelper.SetBearer(client, accessToken);

        var response = await client.PutAsJsonAsync("/v1/customer/cart/b2b", new
        {
            marketCode = "ksa",
            poNumber = "PO-123",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("cart.b2b_fields_forbidden");
    }

    [Fact]
    public async Task SetB2B_B2BAccount_Succeeds()
    {
        await factory.ResetDatabaseAsync();
        var (accessToken, accountId) = await CartCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");

        await using var seedScope = factory.Services.CreateAsyncScope();
        var pricingDb = seedScope.ServiceProvider.GetRequiredService<PricingDbContext>();
        var tier = new B2BTier
        {
            Id = Guid.NewGuid(),
            Slug = "pro",
            Name = "Professional",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        pricingDb.B2BTiers.Add(tier);
        pricingDb.AccountB2BTiers.Add(new AccountB2BTier
        {
            AccountId = accountId,
            TierId = tier.Id,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByAccountId = accountId,
        });
        await pricingDb.SaveChangesAsync();

        var client = factory.CreateClient();
        CartCustomerAuthHelper.SetBearer(client, accessToken);

        var response = await client.PutAsJsonAsync("/v1/customer/cart/b2b", new
        {
            marketCode = "ksa",
            poNumber = "PO-123",
            reference = "REF-XYZ",
            notes = "ship after ramadan",
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, because: await response.Content.ReadAsStringAsync());

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("b2b").GetProperty("poNumber").GetString().Should().Be("PO-123");
    }
}
