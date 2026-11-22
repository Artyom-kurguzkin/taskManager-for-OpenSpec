namespace TaskManager.Tests;

/// <summary>
/// The solution's own source files for the tests that scan the code. A checkout holds tens of thousands of files that are not source
/// (agent worktrees with their own bin and obj, git objects, build output, node_modules), so the walk never enters them.
/// </summary>
internal static class SourceFiles
{
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".claude", ".vs", "bin", "obj", "node_modules", "dist", "local", "TestResults", "out" };

    /// <summary>The folder with RepoTaskManager.slnx, found upwards from the test binaries.</summary>
    public static string Root()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "RepoTaskManager.slnx"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("RepoTaskManager.slnx not found above " + AppContext.BaseDirectory);
    }

    /// <summary>The project folders the solution lists (RepoTaskManager.slnx), the only places source lives.</summary>
    public static IEnumerable<string> ProjectFolders(string root) =>
        System.Xml.Linq.XDocument.Load(Path.Combine(root, "RepoTaskManager.slnx")).Descendants("Project")
            .Select(p => Path.GetDirectoryName(Path.Combine(root, ((string)p.Attribute("Path")!).Replace('/', Path.DirectorySeparatorChar)))!);

    /// <summary>The files under <paramref name="root"/> matching a pattern such as <c>*.cs</c>, without the folders that are not source.</summary>
    public static IEnumerable<string> Of(string root, params string[] patterns)
    {
        Stack<string> folders = new([root]);
        while (folders.Count > 0)
        {
            string folder = folders.Pop();
            foreach (string pattern in patterns)
                foreach (string file in Directory.EnumerateFiles(folder, pattern)) yield return file;
            foreach (string sub in Directory.EnumerateDirectories(folder))
                if (!Skipped.Contains(Path.GetFileName(sub))) folders.Push(sub);
        }
    }
}
