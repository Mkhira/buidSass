using BackendApi.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Tests.Notifications.Support;

/// <summary>
/// In-memory <see cref="NotificationsDbContext"/> factory for handler-level
/// integration tests. Each test gets a unique database name so concurrent
/// xunit threads do not bleed state.
///
/// <para>Note: the in-memory provider does NOT enforce CHECK constraints or
/// FK actions. State-machine + V-1 publish-gate semantics are still verified
/// because they are enforced in handler code, not at the DB layer.</para>
/// </summary>
public static class TestDbFactory
{
    public static NotificationsDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase($"notifications-{Guid.NewGuid():N}")
            .Options;
        return new NotificationsDbContext(options);
    }
}
