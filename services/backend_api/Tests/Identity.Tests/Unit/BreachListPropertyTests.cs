using BackendApi.Modules.Identity.Primitives;
using FsCheck.Xunit;
using FluentAssertions;

namespace Identity.Tests.Unit;

public sealed class BreachListPropertyTests
{
    [Property(MaxTest = 100)]
    public bool HighEntropyCandidate_IsNotCompromised(string candidate)
    {
        var checker = new BreachListChecker();

        var password = $"{(candidate ?? string.Empty).Trim()}::{Guid.NewGuid():N}::{DateTime.UtcNow.Ticks}";
        var compromised = checker.IsCompromised(password);

        compromised.Should().BeFalse();
        return true;
    }
}
