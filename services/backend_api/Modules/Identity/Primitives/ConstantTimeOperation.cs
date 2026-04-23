using System.Diagnostics;

namespace BackendApi.Modules.Identity.Primitives;

public static class ConstantTimeOperation
{
    public static async Task EqualizeAsync(
        Func<Task> operation,
        TimeSpan budget,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        await operation();
        await EnsureMinimumDurationAsync(startedAt, budget, cancellationToken);
    }

    public static async Task<T> EqualizeAsync<T>(
        Func<Task<T>> operation,
        TimeSpan budget,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var result = await operation();
        await EnsureMinimumDurationAsync(startedAt, budget, cancellationToken);
        return result;
    }

    public static async Task EnsureMinimumDurationAsync(
        long startedAtTimestamp,
        TimeSpan minimumDuration,
        CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.GetElapsedTime(startedAtTimestamp);
        var remaining = minimumDuration - elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            return;
        }

        await Task.Delay(remaining, cancellationToken);
    }
}
