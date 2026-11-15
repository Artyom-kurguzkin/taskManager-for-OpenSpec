using System.Globalization;
using GitHistory.Infrastructure;
using GitHistory.PublicDTOs;

namespace GitHistory;

/// <summary>Finds the two sides and the merge base of a file that a git merge left conflict markers in: from the open merge, or from the merge commit that committed the markers.</summary>
internal static class ConflictSidesReader
{
    internal static ConflictSides? Read(string path, string markedText)
    {
        if (!Queries.IsRepository() || GitHistoryModule.RepoRoot is not string root) return null;

        // The state paths git itself names (a worktree keeps them elsewhere): merge, rebase, cherry-pick and revert.
        (int exit, string output, _) = GitCli.Run("rev-parse", "--git-path", "MERGE_HEAD", "--git-path", "rebase-merge", "--git-path", "rebase-apply", "--git-path", "CHERRY_PICK_HEAD", "--git-path", "REVERT_HEAD");
        string[] states = output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (exit != 0 || states.Length != 5) return null;
        string Full(string p) => Path.GetFullPath(p, root);
        if (Directory.Exists(Full(states[1])) || Directory.Exists(Full(states[2])) || File.Exists(Full(states[3])) || File.Exists(Full(states[4]))) return null;   // ours and theirs mean other things there

        string ours, theirs;
        bool inProgress = File.Exists(Full(states[0]));
        if (inProgress)
        {
            string[] heads = File.ReadAllLines(Full(states[0])).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
            if (heads.Length != 1) return null;                                    // an octopus merge has no two sides
            (int e, string head, _) = GitCli.Run("rev-parse", "-q", "--verify", "HEAD^{commit}");
            if (e != 0) return null;
            ours = head.Trim();
            theirs = heads[0];
        }
        else
        {
            // The newest merge commit that changed this file in a way neither parent has: the one that committed the markers. Its file must be the file we hold.
            (int e, string merge, _) = GitCli.Run("log", "--merges", "-1", "--format=%H", "--", path);
            if (e != 0 || merge.Trim() is not { Length: > 0 } m) return null;
            if (Normalize(Blob(m, path) ?? "") != Normalize(markedText)) return null;      // edited after the merge: the hunks in the file are the better source
            (int p, string parents, _) = GitCli.Run("rev-list", "--parents", "-n", "1", m);
            string[] ids = parents.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (p != 0 || ids.Length != 3) return null;
            ours = ids[1];
            theirs = ids[2];
        }

        (int mb, string baseOut, _) = GitCli.Run("merge-base", ours, theirs);
        string baseRev = mb == 0 ? baseOut.Trim() : "";
        if (CommitTime(ours) is not long oursTime || CommitTime(theirs) is not long theirsTime) return null;
        return new ConflictSides(baseRev.Length == 0 ? "" : Blob(baseRev, path) ?? "", Blob(ours, path) ?? "", Blob(theirs, path) ?? "", theirsTime > oursTime, inProgress);
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    /// <summary>The file as that commit has it; null when the commit has no such file.</summary>
    private static string? Blob(string rev, string path)
    {
        (int exit, string output, _) = GitCli.Run("show", rev + ":" + path);
        return exit == 0 ? output : null;
    }

    private static long? CommitTime(string rev)
    {
        (int exit, string output, _) = GitCli.Run("log", "-1", "--format=%ct", rev);
        return exit == 0 && long.TryParse(output.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long t) ? t : null;
    }
}
