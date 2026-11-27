using System.Text;
using System.Text.Json;
using BoardSync;
using BoardSync.PublicDTOs;

namespace TaskManager.Tests;

/// <summary>
/// OpenSpec is incremental: a change is archived, merged or renamed, never deleted. The sync follows a folder that moved, carries edits made meanwhile into where it went,
/// and puts back what is missing with nothing to explain it. Two computers (two directories, two state folders) and one folder storage; paths are relative to openspec/changes.
/// </summary>
public sealed class KeepingEveryChangeTests : IDisposable
{
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("rtm-keep-");
    private DateTime _now = new(2026, 11, 26, 8, 0, 0, DateTimeKind.Utc);

    public void Dispose() { try { _temp.Delete(true); } catch (IOException) { } }

    private void Tick(int minutes = 5) => _now = _now.AddMinutes(minutes);

    private sealed record Computer(string Id, string Root, string State, IBoardStorage Storage);

    private string StorageFolder => Path.Combine(_temp.FullName, "storage");

    private Computer NewComputer(string id) =>
        new(id, Path.Combine(_temp.FullName, id, "changes"), Path.Combine(_temp.FullName, id, "state"), BoardSyncApi.FolderStorage(StorageFolder));

    private Task<SyncReport> Sync(Computer c) => BoardSyncApi.Sync(new SyncOptions(c.Root, c.State, c.Storage, c.Id, () => _now));

    private void Write(Computer c, string path, string text)
    {
        string file = Path.Combine(c.Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text, new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(file, _now);
    }

    private static string Read(Computer c, string path) => File.ReadAllText(Path.Combine(c.Root, path));
    private static bool Exists(Computer c, string path) => File.Exists(Path.Combine(c.Root, path));
    private static bool FolderExists(Computer c, string path) => Directory.Exists(Path.Combine(c.Root, path));

    private static SortedDictionary<string, string> Tree(Computer c) =>
        new(Directory.Exists(c.Root)
            ? Directory.EnumerateFiles(c.Root, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(c.Root, f).Replace('\\', '/'), File.ReadAllText)
            : []);

    private static Dictionary<string, JsonElement> Manifest(Computer c) =>
        JsonDocument.Parse(c.Storage.Get("manifest.json").GetAwaiter().GetResult()!.Bytes).RootElement.GetProperty("entries").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

    private static bool Live(Dictionary<string, JsonElement> manifest, string path) => manifest.TryGetValue(path, out JsonElement e) && !e.GetProperty("deleted").GetBoolean();

    /// <summary>Edits the storage's manifest the way a damaged or reset bucket would: <paramref name="edit"/> sees the entries by path.</summary>
    private void EditManifest(Computer c, Action<Dictionary<string, JsonElement>, Dictionary<string, object?>> edit)
    {
        Dictionary<string, JsonElement> entries = Manifest(c);
        Dictionary<string, object?> result = [];
        edit(entries, result);
        Dictionary<string, object?> all = entries.ToDictionary(e => e.Key, e => (object?)e.Value);
        foreach ((string key, object? value) in result) { if (value is null) all.Remove(key); else all[key] = value; }
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { version = 1, entries = new SortedDictionary<string, object?>(all, StringComparer.Ordinal) });
        c.Storage.Put("manifest.json", bytes, PutCondition.Always).GetAwaiter().GetResult();
    }

    // ---- an item as the app writes it ------------------------------------------------------------------------------------------

    private static string Yaml(int id, string title, string extra = "") =>
        $"schema: spec-driven\ncreated: 2026-11-01\nx-rtm:\n  id: {id}\n  title: {title}\n  status: Planned\n  priority: low\n  contributors: [Ann]\n  commits: [aaa1111]\n{extra}";

    private static string NoteText(int id, string title) => $"---\nid: {id}\nby: Ann\ncreated: 2026-11-10T10:00:00\n---\n# {title}\n\nWhat happened.\n";

    private void Seed(Computer c, string folder, int id, string title)
    {
        Write(c, $"{folder}/.openspec.yaml", Yaml(id, title));
        Write(c, $"{folder}/proposal.md", $"# {title}\n\n## Why\n\nBecause.\n");
        Write(c, $"{folder}/tasks.md", "# Tasks\n\n## 1. Tasks\n\n- [ ] 1.1 first\n- [ ] 1.2 second\n");
        Write(c, $"{folder}/events.log", "2026-11-10T10:00:00+00:00 | Ann | created | seeded\n");
        Write(c, $"{folder}/notes/n{id}1.md", NoteText(id * 10 + 1, $"Note of {title}"));
    }

    /// <summary>What the app's merge leaves on disk: one new folder holding every source (their notes stamped, their tasks in their own group, their events marked), and the sources' folders gone.</summary>
    private void Merge(Computer c, string folder, int id, params (string Folder, int Id, string Title)[] sources)
    {
        string ids = string.Join(", ", sources.Select(s => s.Id)), folders = string.Join(", ", sources.Select(s => s.Folder));
        Write(c, $"{folder}/.openspec.yaml", Yaml(id, sources[0].Title, $"  mergedFrom: [{ids}]\n  mergedFolders: [{folders}]\n"));
        Write(c, $"{folder}/proposal.md", $"# {sources[0].Title}\n\n## Why\n\nBecause.\n");
        StringBuilder tasks = new("# Tasks\n");
        StringBuilder log = new();
        int g = 0;
        foreach ((string f, int sid, string title) in sources)
        {
            g++;
            bool main = g == 1;
            tasks.Append($"\n## {g}. {(main ? title : $"#wi{sid} {title} · merged 2026-11-26")}\n\n- [ ] {g}.1 first\n- [ ] {g}.2 second\n");
            log.Append($"2026-11-10T10:00:00+00:00 | Ann | created | seeded{(main ? "" : $" | from #wi{sid}")}\n");
            string note = Read(c, $"{f}/notes/n{sid}1.md");
            Write(c, $"{folder}/notes/n{sid}1.md", main ? note : note.Replace("by: Ann\n", $"by: Ann\nmergedFrom: {sid}\nmergedAt: 2026-11-26\n"));
            Directory.Delete(Path.Combine(c.Root, f), true);
        }
        foreach ((string _, int sid, string title) in sources.Skip(1)) log.Append($"2026-11-26T08:00:00+00:00 | Ann | merged-from | #wi{sid} {title}\n");
        Write(c, $"{folder}/tasks.md", tasks.ToString());
        Write(c, $"{folder}/events.log", log.ToString());
    }

    private async Task<(Computer A, Computer B)> TwoComputersWith(params (string Folder, int Id, string Title)[] items)
    {
        Computer a = NewComputer("A"), b = NewComputer("B");
        foreach ((string f, int id, string title) in items) Seed(a, f, id, title);
        await Sync(a);
        Tick();
        await Sync(b);
        Tick();
        return (a, b);
    }

    // ---- the tests -------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "The sync never deletes a change")]
    public async Task NeverDeletes()
    {
        (string, int, string)[] items = [.. Enumerable.Range(1, 12).Select(n => ($"item-{n}", 100 + n, $"Item {n}"))];
        (Computer a, Computer b) = await TwoComputersWith(items);
        SortedDictionary<string, string> before = Tree(a);

        foreach ((string folder, int _, string _) in items) Directory.Delete(Path.Combine(b.Root, folder), true);      // nothing says where they went: a checkout, a hand deletion
        SyncReport onB = await Sync(b);

        Assert.Equal(SyncOutcome.Synced, onB.Outcome);                      // no brake: nothing was deleted, so there is nothing to stop
        Assert.Equal(SyncBlock.None, onB.Blocked);
        Assert.Equal(before, Tree(b));                                       // every file is back
        Assert.Empty(onB.DeletedHere);
        Assert.Empty(onB.DeletedInStorage);
        Assert.Contains(onB.Notes, n => n.Kind == "put-back-folder" && n.Folder == "item-3" && n.ItemId == 103);
        Tick();
        await Sync(a);
        Assert.Equal(before, Tree(a));
        Assert.All(Manifest(a), e => Assert.False(e.Value.GetProperty("deleted").GetBoolean()));
    }

    [Fact(DisplayName = "Edits made here are merged into the copy archived elsewhere")]
    public async Task EditsMergedIntoArchivedCopy()
    {
        (Computer a, Computer b) = await TwoComputersWith(("add-health-check", 12, "Add health check"));
        const string archived = "archive/2026-11-20-add-health-check";

        // A archives the item: the folder moves whole, and the log says where.
        Directory.CreateDirectory(Path.Combine(a.Root, "archive"));
        Directory.Move(Path.Combine(a.Root, "add-health-check"), Path.Combine(a.Root, archived));
        File.AppendAllText(Path.Combine(a.Root, archived, "events.log"), "2026-11-20T09:00:00+00:00 | Ann | archived | add-health-check -> archive/2026-11-20-add-health-check\n");
        Tick();
        await Sync(a);

        // B, who has not synced, edits the item meanwhile: the proposal, a tick, a new note and a log line.
        Tick();
        Write(b, "add-health-check/proposal.md", "# Add health check\n\n## Why\n\nBecause.\n\nB added: the probe must answer in 1s.\n");
        Write(b, "add-health-check/tasks.md", "# Tasks\n\n## 1. Tasks\n\n- [x] 1.1 first\n- [ ] 1.2 second\n");
        Write(b, "add-health-check/notes/n900.md", NoteText(900, "Written on B"));
        Write(b, "add-health-check/events.log", Read(b, "add-health-check/events.log") + "2026-11-21T10:00:00+00:00 | Bob | log | probe agreed\n");
        Tick();
        SyncReport onB = await Sync(b);

        Assert.Equal(SyncOutcome.Synced, onB.Outcome);
        Assert.False(FolderExists(b, "add-health-check"), "no active copy comes back");
        Assert.Contains("B added: the probe must answer in 1s.", Read(b, archived + "/proposal.md"));
        Assert.Contains("- [x] 1.1 first", Read(b, archived + "/tasks.md"));
        Assert.Contains("# Written on B", Read(b, archived + "/notes/n900.md"));
        string log = Read(b, archived + "/events.log");
        Assert.Contains("probe agreed", log);
        Assert.Contains("archived | add-health-check", log);
        Assert.NotEmpty(onB.Trashed);                                        // the old active copy is kept in the trash
        Assert.Contains(onB.Notes, n => n.Kind == "followed-archive" && n.Folder == archived && n.ItemId == 12);

        Tick();
        await Sync(a);
        Assert.Equal(Tree(b), Tree(a));
        Assert.False(FolderExists(a, "add-health-check"));
        Dictionary<string, JsonElement> manifest = Manifest(a);
        Assert.DoesNotContain(manifest, e => e.Key.StartsWith("add-health-check/", StringComparison.Ordinal) && !e.Value.GetProperty("deleted").GetBoolean());
        Assert.True(Live(manifest, archived + "/notes/n900.md"));
    }

    private async Task<(Computer A, Computer B)> MergedOnA()
    {
        (Computer a, Computer b) = await TwoComputersWith(("add-health-check", 12, "Add health check"), ("add-ping", 13, "Add ping"));
        Merge(a, "merge-wi12-wi13", 40, ("add-health-check", 12, "Add health check"), ("add-ping", 13, "Add ping"));
        Tick();
        await Sync(a);
        Tick();
        return (a, b);
    }

    [Fact(DisplayName = "Edits made here are carried into the item merged elsewhere")]
    public async Task EditsCarriedIntoMergedItem()
    {
        (Computer a, Computer b) = await MergedOnA();

        // B edits item 13 (add-ping) before it has seen the merge: a log line, a new note, a tick, a new subtask and a new commit.
        Write(b, "add-ping/events.log", Read(b, "add-ping/events.log") + "2026-11-26T09:30:00+00:00 | Bob | log | tuned the ping\n");
        Write(b, "add-ping/notes/n900.md", NoteText(900, "Ping needs a timeout"));
        Write(b, "add-ping/tasks.md", "# Tasks\n\n## 1. Tasks\n\n- [ ] 1.1 first\n- [x] 1.2 second\n- [ ] 1.3 third by B\n");
        Write(b, "add-ping/.openspec.yaml", Yaml(13, "Add ping").Replace("commits: [aaa1111]", "commits: [aaa1111, bbb2222]"));
        Write(b, "add-ping/notes/n131.md", NoteText(131, "Note of Add ping") + "\nB's extra paragraph.\n");      // a note the merge copied, edited here
        Tick();
        SyncReport onB = await Sync(b);

        Assert.Equal(SyncOutcome.Synced, onB.Outcome);
        Assert.False(FolderExists(b, "add-ping"));
        Assert.False(FolderExists(b, "add-health-check"));
        Assert.Contains("tuned the ping | from #wi13", Read(b, "merge-wi12-wi13/events.log"));
        string note = Read(b, "merge-wi12-wi13/notes/n900.md");
        Assert.Contains("# Ping needs a timeout", note);
        Assert.Contains("mergedFrom: 13", note);
        Assert.Contains("mergedAt: ", note);
        string tasks = Read(b, "merge-wi12-wi13/tasks.md");
        Assert.Contains("- [x] 2.2 second", tasks);
        Assert.Contains("- [ ] 2.3 third by B", tasks);
        Assert.Contains("- [ ] 1.2 second", tasks);                         // the other source's group is untouched
        Assert.Contains("commits: [aaa1111, bbb2222]", Read(b, "merge-wi12-wi13/.openspec.yaml"));
        string edited = Read(b, "merge-wi12-wi13/notes/n131.md");
        Assert.Contains("B's extra paragraph.", edited);                    // three-way with the merged item's copy of the note
        Assert.Contains("mergedFrom: 13", edited);
        Assert.NotEmpty(onB.Trashed);
        Assert.Contains(onB.Notes, n => n.Kind == "carried" && n.Folder == "merge-wi12-wi13" && n.ItemId == 40);

        Tick();
        await Sync(a);
        Assert.Equal(Tree(b), Tree(a));
    }

    [Fact(DisplayName = "A title changed after a merge elsewhere becomes a note")]
    public async Task TitleChangedAfterMergeBecomesNote()
    {
        (Computer a, Computer b) = await MergedOnA();

        Write(b, "add-ping/.openspec.yaml", Yaml(13, "Add ping with a timeout"));
        Write(b, "add-ping/proposal.md", "# Add ping with a timeout\n\n## Why\n\nBecause, and a timeout.\n");
        Tick();
        await Sync(b);

        Assert.Contains("title: Add health check", Read(b, "merge-wi12-wi13/.openspec.yaml"));      // Z keeps its own scalars
        Assert.DoesNotContain("with a timeout", Read(b, "merge-wi12-wi13/.openspec.yaml"));
        string[] notes = [.. Directory.EnumerateFiles(Path.Combine(b.Root, "merge-wi12-wi13", "notes"), "n*.md").Select(File.ReadAllText).Where(t => t.Contains("with a timeout"))];
        string note = Assert.Single(notes);
        Assert.Contains("#wi13 was edited on another computer after the merge", note);
        Assert.Contains("title", note);
        Assert.Contains("Because, and a timeout.", note);                   // the proposal's new text is in the note too
        Assert.DoesNotContain("and a timeout", Read(b, "merge-wi12-wi13/proposal.md"));

        Tick();
        await Sync(a);
        Assert.Equal(Tree(b), Tree(a));
    }

    [Fact(DisplayName = "A file gone without a log line is put back")]
    public async Task FileGoneIsPutBack()
    {
        (Computer a, Computer b) = await TwoComputersWith(("add-health-check", 12, "Add health check"));
        string path = "add-health-check/notes/n121.md";
        string text = Read(a, path);

        File.Delete(Path.Combine(b.Root, path));                              // no renumbered, converted or spec-removed line says why
        SyncReport onB = await Sync(b);

        Assert.True(Exists(b, path));
        Assert.Equal(text, Read(b, path));
        Assert.Contains(onB.Notes, n => n.Kind == "put-back-file" && n.Folder == "add-health-check" && n.Text.Contains("notes/n121.md"));
        Assert.Empty(onB.DeletedInStorage);
        Assert.True(Live(Manifest(b), path));
        Tick();
        await Sync(a);
        Assert.Equal(text, Read(a, path));
    }

    [Fact(DisplayName = "A file the log says was removed is removed on the other computer too")]
    public async Task FileTheLogExplainsIsRemoved()
    {
        (Computer a, Computer b) = await TwoComputersWith(("add-health-check", 12, "Add health check"));
        Write(b, "add-health-check/specs/old-cap/spec.md", "## ADDED Requirements\n\n### Requirement: x\nThe system SHALL x.\n");
        await Sync(b);
        Tick();
        await Sync(a);
        Assert.True(Exists(a, "add-health-check/specs/old-cap/spec.md"));

        File.Delete(Path.Combine(b.Root, "add-health-check/specs/old-cap/spec.md"));
        Write(b, "add-health-check/events.log", Read(b, "add-health-check/events.log") + "2026-11-26T09:00:00+00:00 | Bob | spec-removed | specs/old-cap/spec.md\n");
        Tick();
        await Sync(b);
        Tick();
        SyncReport onA = await Sync(a);

        Assert.False(Exists(a, "add-health-check/specs/old-cap/spec.md"));
        Assert.Contains("add-health-check/specs/old-cap/spec.md", onA.DeletedHere);
        Assert.Equal(Tree(b), Tree(a));
    }

    [Fact(DisplayName = "A file the storage lost is uploaded again")]
    public async Task FileTheStorageLostIsUploadedAgain()
    {
        (Computer a, Computer b) = await TwoComputersWith(("add-health-check", 12, "Add health check"));
        string path = "add-health-check/notes/n121.md";
        EditManifest(a, (entries, result) => result[path] = null);            // the bucket no longer lists it at all
        Assert.False(Manifest(a).ContainsKey(path));

        SyncReport onA = await Sync(a);

        Assert.True(Exists(a, path), "the file here is kept");
        Assert.Contains(path, onA.Uploaded);
        Assert.True(Live(Manifest(a), path));
        Assert.Empty(onA.DeletedHere);
        Tick();
        await Sync(b);
        Assert.True(Exists(b, path));
        Assert.Equal(Tree(a), Tree(b));
    }

    [Fact(DisplayName = "A marker nothing explains is ignored")]
    public async Task MarkerNothingExplainsIsIgnored()
    {
        (Computer a, Computer b) = await TwoComputersWith(("add-health-check", 12, "Add health check"), ("add-ping", 13, "Add ping"));
        string path = "add-health-check/notes/n121.md";
        // An old sync left a deleted marker for a note, and for the whole of another folder: no log line, no archive, no merge behind them.
        EditManifest(a, (entries, result) =>
        {
            foreach (string p in entries.Keys.Where(p => p == path || p.StartsWith("add-ping/", StringComparison.Ordinal)))
                result[p] = new { hash = entries[p].GetProperty("hash").GetString(), clock = entries[p].GetProperty("clock").GetString(), deleted = true, history = Array.Empty<string>() };
        });
        Assert.False(Live(Manifest(a), path));

        SyncReport onB = await Sync(b);

        Assert.Equal(SyncOutcome.Synced, onB.Outcome);
        Assert.Empty(onB.DeletedHere);
        Assert.True(Exists(b, path));
        Assert.True(Exists(b, "add-ping/proposal.md"));
        Assert.True(Live(Manifest(b), path));
        Assert.True(Live(Manifest(b), "add-ping/proposal.md"));
        Tick();
        await Sync(a);
        Assert.Equal(Tree(a), Tree(b));
    }

    [Fact(DisplayName = "The same item merged into two items keeps both and notes it")]
    public async Task SameItemMergedTwiceKeepsBoth()
    {
        (Computer a, Computer b) = await TwoComputersWith(("add-health-check", 12, "Add health check"), ("add-ping", 13, "Add ping"), ("add-logs", 14, "Add logs"));
        Merge(a, "merge-wi12-wi13", 40, ("add-health-check", 12, "Add health check"), ("add-ping", 13, "Add ping"));
        Merge(b, "merge-wi12-wi14", 41, ("add-health-check", 12, "Add health check"), ("add-logs", 14, "Add logs"));
        Tick();
        await Sync(a);
        Tick();
        SyncReport onB = await Sync(b);

        Assert.Equal(SyncOutcome.Synced, onB.Outcome);
        Assert.True(FolderExists(b, "merge-wi12-wi13"));
        Assert.True(FolderExists(b, "merge-wi12-wi14"));
        Assert.False(FolderExists(b, "add-health-check"));
        string notesIn(string folder, Computer c) => string.Concat(Directory.EnumerateFiles(Path.Combine(c.Root, folder, "notes"), "n*.md").Select(File.ReadAllText));
        Assert.Contains("#wi12 was also merged into #wi41 on another computer", notesIn("merge-wi12-wi13", b));
        Assert.Contains("#wi12 was also merged into #wi40 on another computer", notesIn("merge-wi12-wi14", b));
        Assert.Contains(onB.Notes, n => n.Kind == "noted" && n.Folder == "merge-wi12-wi13");
        Tick();
        await Sync(a);
        Assert.Equal(Tree(b), Tree(a));
        Assert.Contains("#wi12 was also merged into #wi41", notesIn("merge-wi12-wi13", a));
    }

    [Fact(DisplayName = "Nine items merged elsewhere arrive as one merge and nothing is deleted")]
    public async Task NineItemsMergedElsewhere()
    {
        (string Folder, int Id, string Title)[] items = [.. Enumerable.Range(8, 9).Select(n => ($"item-{n}", n, $"Item {n}"))];
        (Computer a, Computer b) = await TwoComputersWith(items);
        Merge(a, "merge-wi8-wi16", 60, items);
        Tick();
        SyncReport onA = await Sync(a);
        Assert.Equal(SyncOutcome.Synced, onA.Outcome);
        Tick();

        // B has all nine sources unchanged; the storage holds the merged item and a marker for every source file.
        SyncReport onB = await Sync(b);

        Assert.Equal(SyncOutcome.Synced, onB.Outcome);
        Assert.Equal(SyncBlock.None, onB.Blocked);
        Assert.Null(onB.Message);
        Assert.All(items, i => Assert.False(FolderExists(b, i.Folder)));
        Assert.True(Exists(b, "merge-wi8-wi16/.openspec.yaml"));
        Assert.Equal(Tree(a), Tree(b));
        Assert.Equal(items.Length * 5, onB.Trashed.Count);                    // the sources went to the trash, every file
        Assert.All(onB.Trashed, t => Assert.True(File.Exists(t)));
        Assert.Contains(onB.Notes, n => n.Kind == "followed-merge" && n.Folder == "merge-wi8-wi16" && n.ItemId == 60);
    }

    [Fact(DisplayName = "Edits already in the storage are merged into the copy archived here")]
    public async Task StorageEditsMergedIntoLocalArchive()
    {
        (Computer a, Computer b) = await TwoComputersWith(("add-health-check", 12, "Add health check"));
        const string archived = "archive/2026-11-20-add-health-check";

        Write(b, "add-health-check/proposal.md", "# Add health check\n\n## Why\n\nBecause.\n\nB added a line.\n");
        Write(b, "add-health-check/notes/n900.md", NoteText(900, "Written on B"));
        Tick();
        await Sync(b);                                                       // the storage holds B's edits to the active copy
        Tick();

        Directory.CreateDirectory(Path.Combine(a.Root, "archive"));
        Directory.Move(Path.Combine(a.Root, "add-health-check"), Path.Combine(a.Root, archived));      // A archived it without having seen them
        SyncReport onA = await Sync(a);

        Assert.Equal(SyncOutcome.Synced, onA.Outcome);
        Assert.False(FolderExists(a, "add-health-check"));
        Assert.Contains("B added a line.", Read(a, archived + "/proposal.md"));
        Assert.Contains("# Written on B", Read(a, archived + "/notes/n900.md"));
        Assert.DoesNotContain(Manifest(a), e => e.Key.StartsWith("add-health-check/", StringComparison.Ordinal) && !e.Value.GetProperty("deleted").GetBoolean());
        Tick();
        await Sync(b);
        Assert.Equal(Tree(a), Tree(b));
        Assert.False(FolderExists(b, "add-health-check"));
    }

    [Fact(DisplayName = "Edits already in the storage are carried into the item merged here")]
    public async Task StorageEditsCarriedIntoLocalMerge()
    {
        (Computer a, Computer b) = await TwoComputersWith(("add-health-check", 12, "Add health check"), ("add-ping", 13, "Add ping"));
        Write(b, "add-ping/tasks.md", "# Tasks\n\n## 1. Tasks\n\n- [x] 1.1 first\n- [ ] 1.2 second\n- [ ] 1.3 third by B\n");
        Write(b, "add-ping/notes/n900.md", NoteText(900, "Ping needs a timeout"));
        Tick();
        await Sync(b);
        Tick();

        Merge(a, "merge-wi12-wi13", 40, ("add-health-check", 12, "Add health check"), ("add-ping", 13, "Add ping"));
        SyncReport onA = await Sync(a);

        Assert.Equal(SyncOutcome.Synced, onA.Outcome);
        string tasks = Read(a, "merge-wi12-wi13/tasks.md");
        Assert.Contains("- [x] 2.1 first", tasks);
        Assert.Contains("- [ ] 2.3 third by B", tasks);
        Assert.Contains("mergedFrom: 13", Read(a, "merge-wi12-wi13/notes/n900.md"));
        Assert.False(FolderExists(a, "add-ping"));
        Tick();
        await Sync(b);
        Assert.Equal(Tree(a), Tree(b));
        Assert.False(FolderExists(b, "add-ping"));
    }

    [Fact(DisplayName = "Two computers archiving one item on different days keep the earlier folder and merge the other into it")]
    public async Task TwoArchivesKeepTheEarlier()
    {
        (Computer a, Computer b) = await TwoComputersWith(("add-health-check", 12, "Add health check"));
        Directory.CreateDirectory(Path.Combine(a.Root, "archive"));
        Directory.CreateDirectory(Path.Combine(b.Root, "archive"));
        Directory.Move(Path.Combine(a.Root, "add-health-check"), Path.Combine(a.Root, "archive/2026-11-20-add-health-check"));
        Directory.Move(Path.Combine(b.Root, "add-health-check"), Path.Combine(b.Root, "archive/2026-11-22-add-health-check"));
        Write(b, "archive/2026-11-22-add-health-check/proposal.md", "# Add health check\n\n## Why\n\nBecause.\n\nB added a line.\n");
        Tick();
        await Sync(a);
        Tick();
        await Sync(b);
        Tick();
        await Sync(a);

        foreach (Computer c in new[] { a, b })
        {
            Assert.True(FolderExists(c, "archive/2026-11-20-add-health-check"), c.Id);
            Assert.False(FolderExists(c, "archive/2026-11-22-add-health-check"), c.Id);
            Assert.Contains("B added a line.", Read(c, "archive/2026-11-20-add-health-check/proposal.md"));
        }
        Assert.Equal(Tree(a), Tree(b));
    }

    [Fact(DisplayName = "A feature renamed on one computer moves its specs on the other, and nothing else is removed")]
    public async Task SpecMovedIsFollowed()
    {
        (Computer a, Computer b) = await TwoComputersWith(("add-health-check", 12, "Add health check"));
        Write(b, "add-health-check/specs/old-cap/spec.md", "## ADDED Requirements\n\n### Requirement: x\nThe system SHALL x.\n");
        await Sync(b);
        Tick();
        await Sync(a);
        Assert.True(Exists(a, "add-health-check/specs/old-cap/spec.md"));

        Directory.Move(Path.Combine(b.Root, "add-health-check/specs/old-cap"), Path.Combine(b.Root, "add-health-check/specs/new-cap"));
        Write(b, "add-health-check/events.log", Read(b, "add-health-check/events.log") + "2026-11-26T09:00:00+00:00 | Bob | spec-moved | specs/old-cap -> specs/new-cap\n");
        Tick();
        await Sync(b);
        Tick();
        SyncReport onA = await Sync(a);

        Assert.False(Exists(a, "add-health-check/specs/old-cap/spec.md"));
        Assert.True(Exists(a, "add-health-check/specs/new-cap/spec.md"));
        Assert.Equal(["add-health-check/specs/old-cap/spec.md"], onA.DeletedHere);
        Assert.Equal(Tree(b), Tree(a));
    }

    [Fact(DisplayName = "Two computers renaming one folder differently keep the name that sorts first and merge the other into it")]
    public async Task TwoRenamesKeepTheFirstName()
    {
        (Computer a, Computer b) = await TwoComputersWith(("add-health-check", 12, "Add health check"));
        Directory.Move(Path.Combine(a.Root, "add-health-check"), Path.Combine(a.Root, "alpha-check"));
        Directory.Move(Path.Combine(b.Root, "add-health-check"), Path.Combine(b.Root, "beta-check"));
        Write(b, "beta-check/proposal.md", "# Add health check\n\n## Why\n\nBecause.\n\nB added a line.\n");
        Tick();
        await Sync(a);
        Tick();
        await Sync(b);
        Tick();
        await Sync(a);

        foreach (Computer c in new[] { a, b })
        {
            Assert.True(FolderExists(c, "alpha-check"), c.Id);
            Assert.False(FolderExists(c, "beta-check"), c.Id);
            Assert.Contains("B added a line.", Read(c, "alpha-check/proposal.md"));
        }
        Assert.Equal(Tree(a), Tree(b));
    }

    [Fact(DisplayName = "A folder renamed on one computer is followed on the other, with the edits made meanwhile")]
    public async Task RenamedFolderIsFollowed()
    {
        (Computer a, Computer b) = await TwoComputersWith(("add-health-check", 12, "Add health check"));
        Directory.Move(Path.Combine(a.Root, "add-health-check"), Path.Combine(a.Root, "health-check"));
        Tick();
        await Sync(a);
        Tick();
        Write(b, "add-health-check/proposal.md", "# Add health check\n\n## Why\n\nBecause.\n\nB added a line.\n");
        SyncReport onB = await Sync(b);

        Assert.False(FolderExists(b, "add-health-check"));
        Assert.Contains("B added a line.", Read(b, "health-check/proposal.md"));
        Assert.Contains(onB.Notes, n => n.Kind == "followed-rename" && n.Folder == "health-check");
        Tick();
        await Sync(a);
        Assert.Equal(Tree(b), Tree(a));
    }
}
