using OpenSpecStore.PublicDTOs;

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
    public static void Refresh() => OpenSpecStoreModule.BumpOutside();
    /// <summary>How many times a verification listed the tree in this process (a log line and a test count them).</summary>
    public static int Listings => OpenSpecStoreModule.State.Listings;
    /// <summary>
    /// Looks for edits made behind the app's back without reading anything: one listing of the change folders, the main specs and config.yaml, compared to the size and modified time held for each file;
    /// only the files that differ are read again (a racy stamp is checked once more), and <see cref="Changed"/> is raised when one does. Does nothing when the last look is younger than
    /// <paramref name="maxAge"/>. The first look of a repository has nothing to compare with and makes every reader read again. True when something may have changed.
    /// </summary>
    public static bool Verify(TimeSpan? maxAge = null) => OpenSpecStoreModule.Current is { } repo && Verification.Verify(repo, maxAge ?? TimeSpan.Zero);
    public static string? OpenSpecRoot => OpenSpecStoreModule.RepoRoot is null ? null : Path.Combine(OpenSpecStoreModule.RepoRoot, "openspec");
    /// <summary>Opens a repository and makes it current. With <paramref name="keepOthers"/> the other open ones stay open (tabs); otherwise they close.</summary>
    public static (bool Success, string Message) Open(string repoRoot, bool keepOthers = false) => Commands.Open(repoRoot, keepOthers);
    /// <summary>Switches the current repository to another open one; false when it is not open.</summary>
    public static bool Use(string repoRoot) => Commands.Use(repoRoot);
    /// <summary>
    /// Makes <paramref name="repoRoot"/> the current repository for this call only (the code running from here until the scope is disposed): every read and write of the modules, here and in the calls it awaits, goes to it,
    /// while other threads and calls keep the repository they work in. A repository that is not open is used as a guest: it is not added to <see cref="OpenRoots"/>. The folder must exist and hold openspec/.
    /// </summary>
    public static IDisposable InRepo(string repoRoot) => OpenSpecStoreModule.InRepo(repoRoot);

    /// <summary>
    /// An agent's call in the app's own process: what it reads and writes is recorded apart from the window's (until disposed), so the window's stale-write check still sees a file the
    /// call changed since the window read it, and the call is not refused for a file the window read. Pair it with <see cref="InRepo"/>.
    /// </summary>
    public static IDisposable AsCaller() => OpenSpecStoreModule.AsCaller();

    /// <summary>
    /// The repository root for a path inside it (a file or a folder): the nearest folder, walking up, that holds openspec/; null when there is none.
    /// The one rule for "which repository is this path in", for every caller (the app, an agent's tool call).
    /// </summary>
    public static string? FindRepoRoot(string path)
    {
        string full = Path.GetFullPath(path);
        if (File.Exists(full)) full = Path.GetDirectoryName(full)!;
        for (string? dir = full; dir is not null; dir = Path.GetDirectoryName(dir))
            if (Directory.Exists(Path.Combine(dir, "openspec"))) return dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return null;
    }

    /// <summary>Closes the open repositories not listed.</summary>
    public static void Keep(IEnumerable<string> repoRoots) => Commands.Keep(repoRoots);
    /// <summary>Every open repository, in the order opened.</summary>
    public static IReadOnlyList<string> OpenRoots { get { lock (OpenSpecStoreModule.Repos) return OpenSpecStoreModule.Repos.Values.OrderBy(r => r.Opened).Select(r => r.Root).ToList(); } }
    public static void Close() => Commands.Close();

    /// <summary>Told of every exception the file watcher's timer callback catches beyond the expected IO errors (a listener of <see cref="Changed"/> that throws); the callback runs on a pool thread, where a throw would end the process. The app sets it to its log.</summary>
    public static Action<Exception>? BackgroundFault
    {
        get => OpenSpecStoreModule.State.Fault;
        set => OpenSpecStoreModule.State.Fault = value;
    }

    /// <summary>Raised (debounced) when files under openspec/ change on disk, except for the app's own writes.</summary>
    public static event Action? Changed
    {
        add => OpenSpecStoreModule.Changed += value;
        remove => OpenSpecStoreModule.Changed -= value;
    }

    /// <summary>
    /// A multi-step change (a merge) takes this lock for all of its steps, and a reader that repairs what it reads (the notes' duplicate ids) takes it before its own lock,
    /// so a repair never runs on a half-made change: it would see one note in two folders and renumber into an id the change had already picked.
    /// Always taken first, before any module's own lock.
    /// </summary>
    public static object StepsGate => OpenSpecStoreModule.State.StepsGate;

    // ── Ids ──
    /// <summary>
    /// The next free number of a counter ("work-item", "note"), allocated under a lock shared by every git worktree of the repository.
    /// <paramref name="maxOver"/> gets the root of every worktree and returns the highest number it finds in them; the counter file keeps
    /// the last number handed out, so a deleted item never frees its number.
    /// </summary>
    public static int Allocate(string counter, Func<IEnumerable<string>, int> maxOver) =>
        IdAllocator.Allocate(RepoRoot ?? throw new InvalidOperationException("No repository is open"), counter, maxOver);

    // ── References: #wiN, #nN, [[doc]] ──
    /// <summary>Every reference in a text, in order, with its kind ("item", "note", "doc"), target and where it stands. #wi123 is not #wi1234, #wi12a or foo#wi12.</summary>
    public static IReadOnlyList<Reference> FindReferences(string text) => References.Find(text);
    /// <summary>The text of a reference: "#wi12", "#n3" or "[[guide]]" for kind "item", "note" or "doc".</summary>
    public static string FormatReference(string kind, string target) => References.Format(kind, target);
    /// <summary>The text with each reference replaced by what <paramref name="replace"/> returns for it (null keeps it as written).</summary>
    public static string RewriteReferences(string text, Func<Reference, string?> replace) => References.Rewrite(text, replace);
    /// <summary>The text with every reference of kind "item" or "note" numbered <paramref name="from"/> now numbered <paramref name="to"/>; longer numbers that start the same are left alone.</summary>
    public static string RenameReference(string text, string kind, int from, int to) => References.Rename(text, kind, from, to);
    /// <summary>The item a typed reference names ("#wi12", "wi12", and "12" or "#12" when <paramref name="bareNumber"/>), the whole text and no more; null when it names none.</summary>
    public static int? ParseItemReference(string text, bool bareNumber = false) => References.ParseItem(text.Trim(), bareNumber);
    /// <summary>Whether a text (a commit message) mentions the item; for a module that reads texts but must not know the grammar, such as GitHistory, to be handed.</summary>
    public static Func<string, bool> MentionsItem(int id) => References.MentionsItem(id);

    // ── Files ──
    public static string ReadText(string path) => Queries.ReadText(path);
    /// <summary>The file's text (null when it is missing) without recording it for the stale-write check: for a read that only looks (a newest time, a hash) and never backs an edit.</summary>
    public static string? ReadUnrecorded(string path) => Queries.ReadUnrecorded(path);
    /// <summary>The first <paramref name="lines"/> lines of the file (null when it is missing), not recorded: a note's frontmatter.</summary>
    public static string? ReadHead(string path, int lines) => Queries.ReadHead(path, lines);
    public static (bool Success, string Message) WriteText(string path, string content) => Commands.WriteText(path, content);
    /// <summary>Reads the file fresh under the change folder's lock, applies <paramref name="edit"/> to its text and writes that, never refused as stale (append-only files like events.log).</summary>
    public static (bool Success, string Message) UpdateText(string path, Func<string, string> edit) => Commands.UpdateText(path, edit);
    public static (bool Success, string Message) DeleteFolder(string path) => Commands.DeleteFolder(path);
    /// <summary>Deletes one file (nothing to do when it is already gone); the store is told, so every reader sees it gone.</summary>
    public static (bool Success, string Message) DeleteFile(string path) => Commands.DeleteFile(path);
    public static (bool Success, string Message) MoveFolder(string from, string to) => Commands.MoveFolder(from, to);

    // ── x-rtm block ──
    /// <summary>The de-indented YAML under the top-level <c>x-rtm:</c> key, or null when the file has none.</summary>
    public static string? ReadRtmBlock(string yamlPath) => Queries.ReadRtmBlock(yamlPath);

    /// <summary>The de-indented YAML under <c>x-rtm:</c> in text already read, or null when it has none.</summary>
    public static string? ExtractRtmBlock(string fileText) => RtmBlock.Extract(fileText);

    /// <summary>Replaces (or appends) only the <c>x-rtm:</c> block; every other line stays byte-for-byte.</summary>
    public static (bool Success, string Message) WriteRtmBlock(string yamlPath, string blockYaml) => Commands.WriteRtmBlock(yamlPath, blockYaml);

    /// <summary>One section (e.g. "lanes") inside the file's x-rtm block, de-indented; null when absent.</summary>
    public static string? ReadRtmSection(string yamlPath, string key) => Queries.ReadRtmSection(yamlPath, key);

    /// <summary>Replaces one section inside the x-rtm block (null removes it); sibling sections and the rest of the file are untouched.</summary>
    public static (bool Success, string Message) WriteRtmSection(string yamlPath, string key, string? sectionYaml) => Commands.WriteRtmSection(yamlPath, key, sectionYaml);

    // ── The project's settings: x-rtm in openspec/config.yaml ──
    /// <summary>The default folder of note attachments in a project that has no <c>x-rtm.attachmentsDir</c> (nor the old <c>x-rtm.filesDir</c>).</summary>
    public const string DefaultAttachmentsDir = "local/task-tracking";

    /// <summary>The project's config file: the one place its path is built.</summary>
    public static string ConfigFile(string repoRoot) => Queries.ConfigFile(repoRoot);

    /// <summary>
    /// One section of the project's config.yaml (<c>x-rtm.&lt;key&gt;</c>) as <typeparamref name="T"/> (a string, a list of strings, a mapping, ...), of the open project or of <paramref name="repoRoot"/>.
    /// One rule for every section: it never throws. Missing or empty gives no value and no problem; a section that cannot be read gives no value and a problem that names it, so the caller uses its
    /// default and shows the problem; a file that is locked right now is <see cref="PublicDTOs.ConfigSection{T}.Unavailable"/>. For an open project the file is read and parsed once per change of the file, not per call.
    /// </summary>
    public static PublicDTOs.ConfigSection<T> ReadConfigSection<T>(string key, string? repoRoot = null) => Queries.ReadConfigSection<T>(key, repoRoot);

    /// <summary>Whether the app itself wrote this file and it still has the size and modified time that write left (so its text is known, and nothing outside has touched it since): what a cache needs to patch an item from a write instead of reading the repository again.</summary>
    public static bool HoldsWritten(string path) => OpenSpecStoreModule.WrittenText(Path.GetFullPath(path)) is not null;

    /// <summary>Moves each time the open project's files may have changed behind the app's back (the file watcher noticed an edit, or <see cref="Refresh"/>); the app's own writes do not move it, they are in <see cref="ChangesSince"/>.</summary>
    public static long OutsideVersion => OpenSpecStoreModule.Outside;

    /// <summary>Moves each time the open project's config.yaml turned out different from what was read before: what is derived from the file stays valid while it holds still (0 without a project).</summary>
    public static long ConfigVersion => Queries.ConfigVersion(null);

    /// <summary>
    /// Replaces one section of config.yaml with <paramref name="value"/> (null removes it); lists go on one line as [a, b] when <paramref name="flowLists"/>. Sibling sections, comments and the rest of the file
    /// stay as they are. Refused (not written over) when the file changed on disk since this section was read.
    /// </summary>
    public static (bool Success, string Message) WriteConfigSection<T>(string key, T? value, bool flowLists = false, string? repoRoot = null) => Commands.WriteConfigSection(key, value, flowLists, repoRoot);

    /// <summary>Sets one plain value (<c>key: value</c> on one line of the x-rtm block; null removes the key) and touches nothing else. Reads the file from disk first, so it is never refused as stale.</summary>
    public static (bool Success, string Message) WriteConfigValue(string key, string? value, string? repoRoot = null) => Commands.WriteConfigValue(key, value, repoRoot);

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

    /// <summary>Creates a folder (and the missing ones above it) the app itself makes, so the watcher does not report it back as an outside change.</summary>
    public static void CreateFolder(string path) => Commands.CreateOwnedDirectory(path);

    /// <summary>Which repository session is open (goes up on every open, so a closed and reopened one is a new session): what a module's per-repository cache is valid for.</summary>
    public static long RepoGeneration => OpenSpecStoreModule.Current?.Opened ?? 0;

    /// <summary>
    /// The paths the app itself wrote, deleted or moved after position <paramref name="cursor"/> (start with 0), and the position to ask from next time.
    /// A module that caches per folder reads those folders again whatever their timestamps say, since two quick edits can share a timestamp and a size.
    /// </summary>
    public static (IReadOnlyList<string> Paths, long Cursor) ChangesSince(long cursor) => OpenSpecStoreModule.TouchesSince(cursor);
}
