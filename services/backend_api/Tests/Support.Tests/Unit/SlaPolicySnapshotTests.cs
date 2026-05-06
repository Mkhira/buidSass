using BackendApi.Modules.Support.Primitives;
using FluentAssertions;

namespace Support.Tests.Unit;

public class SlaPolicySnapshotTests
{
    [Theory]
    [InlineData("urgent", 15, 240)]
    [InlineData("high", 60, 720)]
    [InlineData("normal", 240, 2880)]
    [InlineData("low", 480, 5760)]
    public void DefaultFor_ReturnsFR021Defaults(string priority, int firstResponse, int resolution)
    {
        var (fr, res) = SlaPolicySnapshot.DefaultFor(priority);
        fr.Should().Be(firstResponse);
        res.Should().Be(resolution);
    }

    [Fact]
    public void DefaultFor_UnknownPriority_Throws()
    {
        var act = () => SlaPolicySnapshot.DefaultFor("nope");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DueUtc_ComputesFromReferenceTime()
    {
        var snapshot = new SlaPolicySnapshot("SA", "normal", 240, 2880);
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        snapshot.FirstResponseDueUtc(now).Should().Be(now.AddMinutes(240));
        snapshot.ResolutionDueUtc(now).Should().Be(now.AddMinutes(2880));
    }
}
