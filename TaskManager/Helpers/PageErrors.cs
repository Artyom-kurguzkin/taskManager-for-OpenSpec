using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace TaskManager.Helpers;

/// <summary>
/// The page's script errors, handed over by the error bridge in wwwroot/js/app.js (<c>window.onerror</c> and <c>unhandledrejection</c>): each becomes one Warning in the run log with message, source, line, column and stack.
/// At most <see cref="PerMinute"/> are logged in a minute, so a loop in the page cannot flood the log: the rest are counted, one line says so, and the count is reported when the minute ends.
/// </summary>
public sealed class PageErrors(TimeProvider clock)
{
    public const int PerMinute = 20;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>The app's one counter, which the page reaches through <see cref="Report"/>.</summary>
    private static readonly PageErrors Shared = new(TimeProvider.System);

    private readonly object gate = new();
    private DateTimeOffset windowStart;
    private bool started;
    private int logged, dropped;

    /// <summary>Called from the page (DotNet.invokeMethodAsync('TaskManager', 'Report', ...)).</summary>
    [JSInvokable]
    public static void Report(string? message, string? source, int line, int column, string? stack) => Shared.Record(message, source, line, column, stack);

    /// <summary>Logs the error unless this minute already held <see cref="PerMinute"/>.</summary>
    public void Record(string? message, string? source, int line, int column, string? stack)
    {
        int missed;
        bool log, notice = false;
        lock (gate)
        {
            DateTimeOffset now = clock.GetUtcNow();
            missed = 0;
            if (!started || now - windowStart >= Window)
            {
                (started, windowStart, missed, logged, dropped) = (true, now, dropped, 0, 0);
            }
            log = logged < PerMinute;
            if (log) logged++;
            else { notice = dropped == 0; dropped++; }
        }
        ILogger logger = AppLog.Logger("Page");
        if (missed > 0) logger.LogWarning("{Count} page errors were dropped in the minute before", missed);
        if (notice) logger.LogWarning("More than {Max} page errors in a minute: the rest are dropped until the minute ends", PerMinute);
        if (log) logger.LogWarning("Page error: {Message} at {Source}:{Line}:{Column}{Stack}", message, source, line, column, string.IsNullOrWhiteSpace(stack) ? "" : Environment.NewLine + stack);
    }
}
