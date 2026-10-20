namespace LinkedTestRuns.Infrastructure;

/// <summary>
/// Repo-relative globs of the form "folder/sub/*.trx" or "folder/**/*.xml" (** = this folder and every subfolder).
/// ponytail: wildcards only in the file name part; a globbing library if someone needs "a*/b".
/// </summary>
internal static class Glob
{
    internal static IEnumerable<string> Find(string repoRoot, string glob)
    {
        string[] parts = glob.Replace('\\', '/').Split('/');
        bool deep = parts.Contains("**");
        string folder = Path.Combine([repoRoot, .. parts[..^1].Where(p => p != "**")]);
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, parts[^1], deep ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            : [];
    }
}
