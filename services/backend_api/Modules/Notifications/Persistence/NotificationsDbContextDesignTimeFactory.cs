using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackendApi.Modules.Notifications.Persistence;

/// <summary>
/// Design-time factory for <c>dotnet ef migrations</c> commands. Uses a
/// hard-coded local connection string that is never touched at runtime.
/// </summary>
public sealed class NotificationsDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=dental;Username=postgres;Password=postgres")
            .Options;
        return new NotificationsDbContext(options);
    }
}
