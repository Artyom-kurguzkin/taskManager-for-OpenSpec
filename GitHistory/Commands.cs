using GitHistory.Infrastructure;

namespace GitHistory;

internal static class Commands
{
    internal static (bool Success, string Message) Commit(IReadOnlyList<string> paths, string message)
    {
        if (!Queries.IsRepository()) return (false, "Not a git repository");
        if (string.IsNullOrWhiteSpace(message)) return (false, "Write a commit message first");
        (int exit, string _, string error) = GitCli.Run(["add", "-A", "--", .. paths]);
        if (exit != 0) return (false, error.Trim());
        (exit, string output, error) = GitCli.Run(["commit", "-q", "-m", message.Trim(), "--", .. paths]);
        return exit == 0 ? (true, "") : (false, (error + output).Trim() is { Length: > 0 } m ? m : "Nothing to commit");
    }
}
