using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Identity.Tests.Contract.Customer;
using Identity.Tests.Infrastructure;

namespace Identity.Tests.Integration;

[Collection("TimingSensitiveIdentityTests")]
public sealed class EnumerationResistanceTests(IdentityTestFactory factory) : IClassFixture<IdentityTestFactory>
{
    [Fact]
    public async Task EnumerationTiming_RegistrationBranchesAreConstantTime()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/v1/customer/identity/register", ValidRegisterRequest("existing@local.dev"));

        var pairDeltas = new List<double>();

        for (var i = 0; i < 12; i++)
        {
            var existingDuration = await MeasureRequestAsync(
                client,
                ValidRegisterRequest("existing@local.dev"));

            var newDuration = await MeasureRequestAsync(
                client,
                ValidRegisterRequest($"new-{i}@local.dev"));

            pairDeltas.Add(Math.Abs(existingDuration - newDuration));
        }

        var p95PairDelta = P95(pairDeltas);
        p95PairDelta.Should().BeLessOrEqualTo(10d);
    }

    private static async Task<double> MeasureRequestAsync(HttpClient client, RegisterRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await client.PostAsJsonAsync("/v1/customer/identity/register", request);
        stopwatch.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return stopwatch.ElapsedMilliseconds;
    }

    private static double P95(List<double> values)
    {
        values.Sort();
        var rank = (int)Math.Ceiling(values.Count * 0.95) - 1;
        rank = Math.Clamp(rank, 0, values.Count - 1);
        return values[rank];
    }

    private static RegisterRequest ValidRegisterRequest(string email) =>
        new(
            Email: email,
            Phone: "+966501234567",
            Password: "StrongPassword!123",
            MarketCode: "ksa",
            Locale: "ar",
            DisplayName: "Contract User");
}
