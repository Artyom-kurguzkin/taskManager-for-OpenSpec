using Microsoft.Extensions.Logging;

namespace TaskManager.Helpers;

/// <summary>The log line of a user action that changed something (#wi178): <c>ui &lt;page&gt; &lt;action&gt; &lt;#wiN | target&gt;</c>, Information. One place writes it; every page and store that changes something on the user's behalf goes through here.</summary>
public static class UiLog
{
    public static void Action(string page, string action, string? target = null) =>
        AppLog.Logger("Ui").LogInformation("{Line}", $"ui {page} {action}{(string.IsNullOrEmpty(target) ? "" : " " + target)}");
}
