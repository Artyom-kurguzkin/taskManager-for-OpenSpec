namespace OpenSpecStore;

/// <summary>
/// The only public type in this assembly. Owns which repository is open, safe reads/writes of its files
/// (an edit is refused when the file changed on disk since the app read it), the app's own <c>x-rtm:</c>
/// block inside YAML files, and live change notifications. No DI; every call delegates to Queries/Commands.
/// </summary>
public static class OpenSpecStoreApi
{
    // ── Workspace ──
    public static string? RepoRoot => OpenSpecStoreModule.RepoRoot;

    /// <summary>Changes whenever repo content may have changed; modules cache their parsed data against it.</summary>
    public static long Version => Interlocked.Read(ref OpenSpecStoreModule.Version);
    public static string? OpenSpecRoot => OpenSpecStoreModule.RepoRoot is null ? null : Path.Combine(OpenSpecStoreModule.RepoRoot, "openspec");
    public static (bool Success, string Message) Open(string repoRoot) => Commands.Open(repoRoot);
    public static void Close() => Commands.Close();

    /// <summary>Raised (debounced) when files under openspec/ change on disk, except for the app's own writes.</summary>
    public static event Action? Changed
    {
        add => OpenSpecStoreModule.Changed += value;
        remove => OpenSpecStoreModule.Changed -= value;
    }

    // ── Files ──
    public static string ReadText(string path) => Queries.ReadText(path);
    public static (bool Success, string Message) WriteText(string path, string content) => Commands.WriteText(path, content);
    public static (bool Success, string Message) DeleteFolder(string path) => Commands.DeleteFolder(path);

    // ── x-rtm block ──
    /// <summary>The de-indented YAML under the top-level <c>x-rtm:</c> key, or null when the file has none.</summary>
    public static string? ReadRtmBlock(string yamlPath) => Queries.ReadRtmBlock(yamlPath);

    /// <summary>Replaces (or appends) only the <c>x-rtm:</c> block; every other line stays byte-for-byte.</summary>
    public static (bool Success, string Message) WriteRtmBlock(string yamlPath, string blockYaml) => Commands.WriteRtmBlock(yamlPath, blockYaml);

    /// <summary>One section (e.g. "lanes") inside the file's x-rtm block, de-indented; null when absent.</summary>
    public static string? ReadRtmSection(string yamlPath, string key) => Queries.ReadRtmSection(yamlPath, key);

    /// <summary>Replaces one section inside the x-rtm block (null removes it); sibling sections and the rest of the file are untouched.</summary>
    public static (bool Success, string Message) WriteRtmSection(string yamlPath, string key, string? sectionYaml) => Commands.WriteRtmSection(yamlPath, key, sectionYaml);

    /// <summary>Tells the watcher that the app itself is about to change this path (e.g. through a CLI it runs).</summary>
    public static void MarkOwnChange(string path) => OpenSpecStoreModule.MarkOwn(path);
}
