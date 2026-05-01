using FluentAssertions;

namespace Reviews.Tests.Unit.CiGuards;

/// <summary>
/// Spec 022 T147 — project-memory rule: every per-module DbContext + every
/// AddDbContext registration MUST suppress <c>ManyServiceProvidersCreatedWarning</c>
/// or Identity tests break under WebApplicationFactory.
///
/// This test acts as the CI guard: it greps the Reviews module's
/// <c>ReviewsDbContext.cs</c> + <c>ReviewsModule.cs</c> for the suppression
/// directive and fails the build if either is missing.
/// </summary>
public sealed class ManyServiceProvidersCreatedWarningSuppressionTests
{
    private const string Marker = "ManyServiceProvidersCreatedWarning";

    [Fact]
    public void ReviewsDbContext_suppresses_warning_in_OnConfiguring()
    {
        var path = ResolveModuleFile("Persistence", "ReviewsDbContext.cs");
        var content = File.ReadAllText(path);
        content.Should().Contain(Marker,
            "ReviewsDbContext.OnConfiguring MUST suppress ManyServiceProvidersCreatedWarning per project-memory rule.");
    }

    [Fact]
    public void ReviewsModule_suppresses_warning_in_AddDbContext()
    {
        var path = ResolveModuleFile("ReviewsModule.cs");
        var content = File.ReadAllText(path);
        var occurrences = CountOccurrences(content, Marker);
        occurrences.Should().BeGreaterThanOrEqualTo(2,
            "ReviewsModule.cs registers BOTH AddDbContext and AddDbContextFactory; each call MUST configure the warning suppression (belt-and-braces with the OnConfiguring guard).");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    private static string ResolveModuleFile(params string[] segments)
    {
        // Tests run from .../bin/Debug/net9.0/. Walk up to the repo root and
        // descend into the Reviews module directory.
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12; i++)
        {
            var candidate = Path.Combine(new[] { dir, "services", "backend_api", "Modules", "Reviews" }
                .Concat(segments).ToArray());
            if (File.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent is null || parent == dir) break;
            dir = parent;
        }
        throw new FileNotFoundException(
            $"Could not locate Modules/Reviews/{string.Join('/', segments)} from test base.");
    }
}
