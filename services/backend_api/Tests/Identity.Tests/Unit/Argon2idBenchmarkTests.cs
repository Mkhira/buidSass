using System.Diagnostics;
using BackendApi.Modules.Identity.Primitives;
using FluentAssertions;

namespace Identity.Tests.Unit;

public sealed class Argon2idBenchmarkTests
{
    [Fact]
    public void Argon2id_HashAndVerify_P95WithinAdvisoryThreshold()
    {
        var hasher = new Argon2idHasher();
        const string password = "BenchmarkPassword!123";

        var hashSamples = new List<double>();
        var verifySamples = new List<double>();

        for (var i = 0; i < 8; i++)
        {
            var swHash = Stopwatch.StartNew();
            var hash = hasher.HashPassword(password, SurfaceKind.Customer);
            swHash.Stop();
            hashSamples.Add(swHash.Elapsed.TotalMilliseconds);

            var swVerify = Stopwatch.StartNew();
            var verify = hasher.VerifyAndRehashIfNeeded(password, hash, SurfaceKind.Customer);
            swVerify.Stop();
            verifySamples.Add(swVerify.Elapsed.TotalMilliseconds);
            verify.IsValid.Should().BeTrue();
        }

        var hashP95 = Percentile(hashSamples, 0.95);
        var verifyP95 = Percentile(verifySamples, 0.95);

        // Advisory threshold to surface severe tuning drifts in CI while avoiding host noise flakes.
        hashP95.Should().BeLessOrEqualTo(6000, $"hash p95 drifted high ({hashP95:F2} ms)");
        verifyP95.Should().BeLessOrEqualTo(6000, $"verify p95 drifted high ({verifyP95:F2} ms)");
    }

    private static double Percentile(List<double> samples, double p)
    {
        samples.Sort();
        var index = (int)Math.Ceiling((samples.Count - 1) * p);
        return samples[index];
    }
}
