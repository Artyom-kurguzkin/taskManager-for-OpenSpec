using System.Security.Cryptography;
using System.Text;

namespace OpenSpecStore;

/// <summary>
/// Hands out work-item and note numbers under a cross-process lock that every git worktree of one repository shares
/// (it lives in the git common dir), so two writers never get the same number and a deleted item never frees its number.
/// </summary>
internal static class IdAllocator
{
    internal static int Allocate(string root, string counter, Func<IEnumerable<string>, int> maxOver)
    {
        string common = CommonDir(root);
        string dir = Path.Combine(common, "rtm");
        Directory.CreateDirectory(dir);
        string counterPath = Path.Combine(dir, counter);

        using FileStream held = Lock(Path.Combine(dir, counter + ".lock"));   // released when this method returns or throws; a crashed holder's lock dies with its process
        int last = int.TryParse(File.Exists(counterPath) ? File.ReadAllText(counterPath).Trim() : "", out int n) ? n : 0;
        int next = Math.Max(last, maxOver(Roots(root, common))) + 1;
        File.WriteAllText(counterPath, next.ToString());
        return next;
    }

    // ponytail: poll with doubling backoff on a plain file lock instead of a named mutex: the file lives in the shared git dir, so it works across worktrees and processes.
    private static FileStream Lock(string path)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(5);
        for (int delay = 25; ; delay = Math.Min(delay * 2, 400))
        {
            try
            {
                FileStream fs = new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                fs.SetLength(0);
                fs.Write(Encoding.UTF8.GetBytes($"pid {Environment.ProcessId}"));
                fs.Flush();
                return fs;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)   // Windows reports a held file as either
            {
                if (DateTime.UtcNow >= until) throw new TimeoutException($"Could not take the id lock {path} within 5 seconds; another process holds it", e);
                Thread.Sleep(delay);
            }
        }
    }

    /// <summary>The git common dir, read from .git files (no process): a .git folder is it; a .git file points into &lt;common&gt;/worktrees/&lt;name&gt;. Without git, a folder in the temp dir keyed by the repository path.</summary>
    internal static string CommonDir(string root)
    {
        string dotGit = Path.Combine(root, ".git");
        if (Directory.Exists(dotGit)) return dotGit;
        if (File.Exists(dotGit) && File.ReadAllText(dotGit).Trim() is { } line && line.StartsWith("gitdir:", StringComparison.Ordinal))
        {
            string target = Path.GetFullPath(line["gitdir:".Length..].Trim(), root);
            if (Directory.GetParent(target)?.Parent is { } common) return common.FullName;   // ponytail: assumes <common>/worktrees/<name>, which is what git writes
        }
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root)))).ToLowerInvariant()[..16];
        return Path.Combine(Path.GetTempPath(), "rtm", hash);
    }

    /// <summary>The current root plus the main worktree and every linked worktree that still exists.</summary>
    private static List<string> Roots(string root, string common)
    {
        List<string> roots = [Path.GetFullPath(root)];
        void Add(string? r) { if (r is not null && Directory.Exists(r) && !roots.Contains(r, StringComparer.OrdinalIgnoreCase)) roots.Add(r); }
        if (!Directory.Exists(Path.Combine(common, "objects"))) return roots;   // not a real git dir (no git, or a bare .git folder): just this root
        Add(Directory.GetParent(common)?.FullName);
        string worktrees = Path.Combine(common, "worktrees");
        if (Directory.Exists(worktrees))
            foreach (string w in Directory.GetDirectories(worktrees))
            {
                string file = Path.Combine(w, "gitdir");   // holds the path of that worktree's .git file; its parent is the worktree root
                if (File.Exists(file)) Add(Path.GetDirectoryName(Path.GetFullPath(File.ReadAllText(file).Trim())));
            }
        return roots;
    }
}
