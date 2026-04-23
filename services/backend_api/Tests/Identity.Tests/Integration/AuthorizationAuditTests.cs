using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Identity.Tests.Contract.Customer;
using Identity.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Integration;

public sealed class AuthorizationAuditTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task AdminWithoutPermission_Returns403AndAuditRow()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        var seed = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "admin-no-permission@local.dev",
            password: "AdminOnly!12345",
            permissions: [],
            withTotpFactor: true,
            roleCode: "platform.support");

        var adminToken = await AdminTestDataHelper.SignInAndCompleteMfaAsync(
            client,
            seed.Email,
            seed.Password,
            seed.TotpSecret!);

        AdminTestDataHelper.SetBearer(client, adminToken);
        var response = await client.GetAsync("/v1/admin/identity/_test/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var scope = factory.Services.CreateAsyncScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditRows = await appDb.AuditLogEntries
            .Where(x => x.Action == "identity.authorization.decision")
            .OrderByDescending(x => x.OccurredAt)
            .Take(5)
            .ToListAsync();

        auditRows.Should().NotBeEmpty();
        auditRows.Any(x => x.ActorId == seed.AccountId).Should().BeTrue();

        var authRows = await identityDb.AuthorizationAudits
            .Where(x => x.AccountId == seed.AccountId && x.Decision == "deny")
            .ToListAsync();
        authRows.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AuthorizationAudit_EveryDenyHasRow()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
        });

        var seed = await AdminTestDataHelper.SeedAdminAsync(
            factory,
            email: "admin-deny-audit@local.dev",
            password: "AdminOnly!12345",
            permissions: [],
            withTotpFactor: true,
            roleCode: "platform.support");

        var token = await AdminTestDataHelper.SignInAndCompleteMfaAsync(
            client,
            seed.Email,
            seed.Password,
            seed.TotpSecret!);

        AdminTestDataHelper.SetBearer(client, token);
        var correlationId = Guid.NewGuid().ToString("D");
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        var deny1 = await client.GetAsync("/v1/admin/identity/_test/protected");
        var deny2 = await client.GetAsync("/v1/admin/identity/_test/protected");
        var deny3 = await client.GetAsync("/v1/admin/identity/_test/protected");

        deny1.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        deny2.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        deny3.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var scope = factory.Services.CreateAsyncScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rows = await appDb.AuditLogEntries
            .Where(x => x.Action == "identity.authorization.decision"
                        && x.CorrelationId.ToString() == correlationId
                        && x.ActorId == seed.AccountId)
            .ToListAsync();

        rows.Count.Should().BeGreaterOrEqualTo(3);

        var authorizationRows = await identityDb.AuthorizationAudits
            .Where(x => x.AccountId == seed.AccountId
                        && x.CorrelationId.ToString() == correlationId
                        && x.Decision == "deny")
            .ToListAsync();
        authorizationRows.Count.Should().BeGreaterOrEqualTo(3);
    }
}
