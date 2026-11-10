using GitHistory;

namespace TaskManager.Services;

/// <summary>
/// Each work item's worklog is <c>events.log</c> in its change folder, and a common ignore rule (<c>*.log</c>) keeps them all out of git.
/// On open, git is asked whether such a file would be ignored; if so the repository's .gitignore gets an exception.
/// </summary>
public static class WorklogTracking
{
    private const string Rule = "!openspec/**/events.log";
    private const string Probe = "openspec/changes/x/events.log";

    /// <summary>
    /// Run with GitHistory pointed at <paramref name="root"/>. Returns what to tell the user, or null when nothing was ignored (or this is no git repository).
    /// The exception line, once in the file, is the "already tried" marker: a rule that cannot be overridden this way is reported, never appended again.
    /// </summary>
    public static string? Ensure(string root)
    {
        if (!GitHistoryApi.IsIgnored(Probe)) return null;
        string file = Path.Combine(root, ".gitignore");
        string text = File.Exists(file) ? File.ReadAllText(file) : "";
        if (!text.Replace("\r", "").Split('\n').Any(l => l.Trim() == Rule))
        {
            string nl = text.Contains("\r\n") ? "\r\n" : "\n";
            string lead = text.Length == 0 ? "" : text.EndsWith('\n') ? nl : nl + nl;
            File.AppendAllText(file, $"{lead}# Repo Tasks: the worklog of each work item is data, not a log file to ignore{nl}{Rule}{nl}");
            if (!GitHistoryApi.IsIgnored(Probe)) return "events.log files were ignored by git; added an exception to .gitignore. Run git add openspec to track the worklog.";
        }
        return $"The worklog (events.log) of {Path.GetFileName(root)} is still ignored by git. Look at its ignore rules: a parent folder may be ignored, or a later rule may win over the exception in .gitignore.";
    }
}
