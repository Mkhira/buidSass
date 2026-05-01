using BackendApi.Modules.Reviews.Primitives;
using FluentAssertions;

namespace Reviews.Tests.Unit.Primitives;

/// <summary>Spec 022 T046 — canonical reviewer-display rule per FR-016a.</summary>
public sealed class ReviewerDisplayRendererTests
{
    [Theory]
    [InlineData("DentalPro", "Sara", "Khan", "DentalPro")]    // handle wins
    [InlineData(null, "Sara", "Khan", "Sara K.")]              // first + last initial
    [InlineData("", "Sara", "Khan", "Sara K.")]                // empty handle == null
    [InlineData(null, "Sara", "", "Sara")]                     // missing last name → first only
    [InlineData(null, "", "Khan", "K.")]                       // missing first → initial only
    [InlineData(null, "", "", "—")]                            // both missing → em-dash placeholder
    [InlineData("   ", "Sara", "Khan", "Sara K.")]             // whitespace-only handle treated as null
    [InlineData(null, "Mohamed", "أحمد", "Mohamed أ.")]        // mixed locale
    public void Renders_per_table(string? handle, string firstName, string lastName, string expected)
    {
        ReviewerDisplayRenderer.Render(handle, firstName, lastName).Should().Be(expected);
    }
}
