using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Workers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Verification.Tests.Integration;

/// <summary>
/// Spec 020 task T092 / research §R12. Verifies the Postgres advisory-lock
/// pattern: two concurrent worker instances trying to take the same lock
/// → only one acquires; the other no-ops cleanly. Releasing the lock allows
/// the next caller to acquire.
/// </summary>
public sealed class WorkerAdvisoryLockTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_advisory_lock_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    private string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = NewContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private VerificationDbContext NewContext() => new(
        new DbContextOptionsBuilder<VerificationDbContext>().UseNpgsql(ConnectionString).Options);

    [Fact]
    public async Task Two_concurrent_acquires_yield_only_one_winner()
    {
        await using var dbA = NewContext();
        await using var dbB = NewContext();

        await using var lockA = await PostgresAdvisoryLock.TryAcquireAsync(
            dbA, PostgresAdvisoryLock.Keys.ExpiryWorker, CancellationToken.None);
        await using var lockB = await PostgresAdvisoryLock.TryAcquireAsync(
            dbB, PostgresAdvisoryLock.Keys.ExpiryWorker, CancellationToken.None);

        lockA.Acquired.Should().BeTrue("first caller wins the lock");
        lockB.Acquired.Should().BeFalse("second caller must observe the lock as held — clean no-op");
    }

    [Fact]
    public async Task Released_lock_can_be_reacquired()
    {
        await using var db = NewContext();

        var first = await PostgresAdvisoryLock.TryAcquireAsync(
            db, PostgresAdvisoryLock.Keys.ReminderWorker, CancellationToken.None);
        first.Acquired.Should().BeTrue();
        await first.DisposeAsync();

        var second = await PostgresAdvisoryLock.TryAcquireAsync(
            db, PostgresAdvisoryLock.Keys.ReminderWorker, CancellationToken.None);
        try
        {
            second.Acquired.Should().BeTrue("after the first holder disposes, the lock is free");
        }
        finally
        {
            await second.DisposeAsync();
        }
    }

    [Fact]
    public async Task Different_lock_keys_do_not_block_each_other()
    {
        await using var dbA = NewContext();
        await using var dbB = NewContext();

        await using var expiryLock = await PostgresAdvisoryLock.TryAcquireAsync(
            dbA, PostgresAdvisoryLock.Keys.ExpiryWorker, CancellationToken.None);
        await using var reminderLock = await PostgresAdvisoryLock.TryAcquireAsync(
            dbB, PostgresAdvisoryLock.Keys.ReminderWorker, CancellationToken.None);

        expiryLock.Acquired.Should().BeTrue();
        reminderLock.Acquired.Should().BeTrue("each worker has its own key — they're independent");
    }
}
