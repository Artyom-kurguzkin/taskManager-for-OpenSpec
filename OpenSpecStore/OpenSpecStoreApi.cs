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
    public static long Version => OpenSpecStoreModule.Version;
    /// <summary>What the open repositories' content versions add up to: unlike <see cref="Version"/> it moves when a write lands in a repository that is open but not the current one.</summary>
    public static long TotalVersion { get { lock (OpenSpecStoreModule.Repos) return OpenSpecStoreModule.Repos.Values.Sum(r => Interlocked.Read(ref r.Version)); } }
    /// <summary>Makes every module read the current repository from disk again on its next call, for a caller that cannot wait for the watcher (a tool call right after an outside edit).</summary>
    public static void Refresh() => OpenSpecStoreModule.Bump();
    public static string? OpenSpecRoot => OpenSpecStoreModule.RepoRoot is null ? null : Path.Combine(OpenSpecStoreModule.RepoRoot, "openspec");
    /// <summary>Opens a repository and makes it current. With <paramref name="keepOthers"/> the other open ones stay open (tabs); otherwise they close.</summary>
    public static (bool Success, string Message) Open(string repoRoot, bool keepOthers = false) => Commands.Open(repoRoot, keepOthers);
    /// <summary>Switches the current repository to another open one; false when it is not open.</summary>
    public static bool Use(string repoRoot) => Commands.Use(repoRoot);
    /// <summary>Closes the open repositories not listed.</summary>
    public static void Keep(IEnumerable<string> repoRoots) => Commands.Keep(repoRoots);
    /// <summary>Every open repository, in the order opened.</summary>
    public static IReadOnlyList<string> OpenRoots { get { lock (OpenSpecStoreModule.Repos) return OpenSpecStoreModule.Repos.Values.OrderBy(r => r.Opened).Select(r => r.Root).ToList(); } }
    public static void Close() => Commands.Close();

    /// <summary>Raised (debounced) when files under openspec/ change on disk, except for the app's own writes.</summary>
    public static event Action? Changed
    {
        add => OpenSpecStoreModule.Changed += value;
        remove => OpenSpecStoreModule.Changed -= value;
    }

    // ── Ids ──
    /// <summary>
    /// The next free number of a counter ("work-item", "note"), allocated under a lock shared by every git worktree of the repository.
    /// <paramref name="maxOver"/> gets the root of every worktree and returns the highest number it finds in them; the counter file keeps
    /// the last number handed out, so a deleted item never frees its number.
    /// </summary>
    public static int Allocate(string counter, Func<IEnumerable<string>, int> maxOver) =>
        IdAllocator.Allocate(RepoRoot ?? throw new InvalidOperationException("No repository is open"), counter, maxOver);

    // ── Files ──
    public static string ReadText(string path) => Queries.ReadText(path);
    public static (bool Success, string Message) WriteText(string path, string content) => Commands.WriteText(path, content);
    public static (bool Success, string Message) DeleteFolder(string path) => Commands.DeleteFolder(path);
    public static (bool Success, string Message) MoveFolder(string from, string to) => Commands.MoveFolder(from, to);

    // ── x-rtm block ──
    /// <summary>The de-indented YAML under the top-level <c>x-rtm:</c> key, or null when the file has none.</summary>
    public static string? ReadRtmBlock(string yamlPath) => Queries.ReadRtmBlock(yamlPath);

    /// <summary>Replaces (or appends) only the <c>x-rtm:</c> block; every other line stays byte-for-byte.</summary>
    public static (bool Success, string Message) WriteRtmBlock(string yamlPath, string blockYaml) => Commands.WriteRtmBlock(yamlPath, blockYaml);

    /// <summary>One section (e.g. "lanes") inside the file's x-rtm block, de-indented; null when absent.</summary>
    public static string? ReadRtmSection(string yamlPath, string key) => Queries.ReadRtmSection(yamlPath, key);

    /// <summary>Replaces one section inside the x-rtm block (null removes it); sibling sections and the rest of the file are untouched.</summary>
    public static (bool Success, string Message) WriteRtmSection(string yamlPath, string key, string? sectionYaml) => Commands.WriteRtmSection(yamlPath, key, sectionYaml);

    /// <summary>
    /// The serializer for every YAML the app writes: strings are quoted when YAML would read them as something else (colons, hashes, a leading
    /// indicator, yes/no/null, numbers, empty or multi-line text) and left plain otherwise. <paramref name="model"/>: camelCase names, nulls and empty lists left out.
    /// <paramref name="flowLists"/>: lists as [a, b] on one line.
    /// </summary>
    public static YamlDotNet.Serialization.ISerializer YamlWriter(bool model, bool flowLists) => YamlText.Writer(model, flowLists);

    /// <summary>
    /// Mends a block of hand-written YAML: a <c>key: value</c> line (top-level, one of <paramref name="textKeys"/>) that is not valid YAML on its own, like
    /// <c>title: Fix: the thing</c>, has its value quoted. Valid lines stay as they are. Returns the text and the 1-based line of each mended line.
    /// </summary>
    public static (string Text, IReadOnlyList<(string Key, int Line)> Mended) MendYaml(string text, IReadOnlySet<string> textKeys) => YamlText.Mend(text, textKeys);

    /// <summary>Tells the watcher that the app itself is about to change this path (e.g. through a CLI it runs).</summary>
    public static void MarkOwnChange(string path) => OpenSpecStoreModule.MarkOwn(path);

    /// <summary>Which repository session is open (goes up on every open, so a closed and reopened one is a new session): what a module's per-repository cache is valid for.</summary>
    public static long RepoGeneration => OpenSpecStoreModule.Current?.Opened ?? 0;

    /// <summary>
    /// The paths the app itself wrote, deleted or moved after position <paramref name="cursor"/> (start with 0), and the position to ask from next time.
    /// A module that caches per folder reads those folders again whatever their timestamps say, since two quick edits can share a timestamp and a size.
    /// </summary>
    public static (IReadOnlyList<string> Paths, long Cursor) ChangesSince(long cursor) => OpenSpecStoreModule.TouchesSince(cursor);
}
