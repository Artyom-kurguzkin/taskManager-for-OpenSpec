namespace TaskManager.Services;

/// <summary>
/// Two computers that commit the same synced board can still differ in the worklog, a file that only ever grows. A union merge keeps both sides'
/// lines, so git never reports a conflict in an events.log. Added to the repository's .gitattributes when a project connects to a storage.
/// </summary>
public static class GitAttributes
{
    public const string Rule = "openspec/**/events.log merge=union";

    /// <summary>Makes sure <paramref name="root"/>'s .gitattributes holds the rule (created when missing). Returns what to tell the user when it was added, null otherwise; nothing happens outside a git repository.</summary>
    public static string? Ensure(string root)
    {
        string git = Path.Combine(root, ".git");
        if (!Directory.Exists(git) && !File.Exists(git)) return null;   // a worktree has a .git file
        string file = Path.Combine(root, ".gitattributes");
        // Never written through a link or into a folder: a repository can make .gitattributes point at any file the user can write (the rule would be appended there).
        FileInfo info = new(file);
        if (Directory.Exists(file) || info.LinkTarget is not null || (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint)))
            return "The repository's .gitattributes is a link or a folder, so it was left alone. Add this line to a real .gitattributes yourself so git merges the worklogs without conflicts: " + Rule;
        string text = File.Exists(file) ? File.ReadAllText(file) : "";
        if (text.Replace("\r", "").Split('\n').Any(l => l.Trim() == Rule)) return null;
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
        string lead = text.Length == 0 ? "" : text.EndsWith('\n') ? nl : nl + nl;
        File.AppendAllText(file, $"{lead}# Repo Tasks: a worklog only grows, so two computers' lines are both kept{nl}{Rule}{nl}");
        return "Added a union-merge rule for the worklogs (events.log) to .gitattributes; commit it so git merges them without conflicts.";
    }
}
