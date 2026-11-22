using System.Globalization;
using Notes;
using OpenSpecStore;
using TaskManager.AppState;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// Headers first, bodies on demand: the board is built from each change's .openspec.yaml and tasks.md (and the worklog's newest time, for "updated"); a proposal, a note, the events and a spec
/// are read when a page asks for them, into a cache that holds a bounded number of items. A file is read again only when its size or modified time moved (git's index rule), and a file
/// whose time is the time it was read is checked once more (racy-git). The store records the files it opened (<c>StoreState.ReadLog</c>), so these tests count opens, not time.
/// </summary>
public sealed class ReadingOnlyWhatIsNeededTests
{
    private const int Folders = 300;
    private static readonly DateTime Old = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Lazy<string> Shared = new(() => Generate(Path.Combine(Path.GetTempPath(), "rtm-tests", "headers-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..6])));

    static ReadingOnlyWhatIsNeededTests() =>
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { if (Shared.IsValueCreated) Directory.Delete(Shared.Value, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } };

    /// <summary>A repository of 300 changes, each with yaml, proposal, tasks, three notes, an event log and a spec; every file is old (a repository somebody has been using, not one just checked out).</summary>
    private static string Generate(string root)
    {
        string changes = Path.Combine(root, "openspec", "changes");
        Parallel.For(1, Folders + 1, i =>
        {
            string dir = Path.Combine(changes, $"change-{i:D3}");
            Directory.CreateDirectory(Path.Combine(dir, "notes"));
            Directory.CreateDirectory(Path.Combine(dir, "specs", $"cap-{i:D3}"));
            File.WriteAllText(Path.Combine(dir, ".openspec.yaml"), $"schema: spec-driven\ncreated: 2025-12-01\nx-rtm:\n  id: {i}\n  title: Change {i}\n  status: {(i % 3 == 0 ? "In Progress" : "Planned")}\n  priority: {(i % 2 == 0 ? "high" : "low")}\n");
            File.WriteAllText(Path.Combine(dir, "proposal.md"), $"# Change {i}\n\nWhy change {i} exists.\n");
            File.WriteAllText(Path.Combine(dir, "tasks.md"), "## 1. Work\n\n- [x] 1.1 First\n- [ ] 1.2 Second\n- [ ] 1.3 Third\n");
            File.WriteAllText(Path.Combine(dir, "events.log"), $"2026-02-0{1 + i % 5}T08:00:00 | Tom | created | Change {i}\n");
            File.WriteAllText(Path.Combine(dir, "specs", $"cap-{i:D3}", "spec.md"), $"## ADDED Requirements\n\n### Requirement: Thing {i}\nThe app SHALL do {i}.\n\n#### Scenario: A\n- **WHEN** x\n- **THEN** y\n");
            for (int n = 1; n <= 3; n++)
                File.WriteAllText(Path.Combine(dir, "notes", $"n{i * 10 + n}.md"), $"---\nid: {i * 10 + n}\nby: Tom\ncreated: 2025-11-2{n}T09:00:00\n---\n# Note {n} of {i}\n\nText.\n");
            foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetLastWriteTimeUtc(file, Old);
        });
        Directory.CreateDirectory(Path.Combine(root, "openspec", "specs"));
        return root;
    }

    /// <summary>Runs an edit of one file of the shared repository and puts the file back as it was (text and old time) afterwards, so the tests that only read never see it.</summary>
    private static void Editing(string file, Action edit)
    {
        string text = File.ReadAllText(file);
        try { edit(); }
        finally { File.WriteAllText(file, text); File.SetLastWriteTimeUtc(file, Old); }
    }

    private static string Dir(string root, int i) => Path.Combine(root, "openspec", "changes", $"change-{i:D3}");

    private static List<string> StartRecording()
    {
        List<string> log = [];
        OpenSpecStoreModule.State.ReadLog = log;
        return log;
    }

    private static List<string> Opened(List<string> log, string root)
    {
        string changes = Path.Combine(root, "openspec", "changes") + Path.DirectorySeparatorChar;
        lock (log) return [.. log.Where(p => p.StartsWith(changes, StringComparison.OrdinalIgnoreCase)).Select(p => p[changes.Length..].Replace('\\', '/'))];
    }

    private static string What(string relative) => relative[(relative.IndexOf('/') + 1)..];

    [Fact(DisplayName = "Building the board reads only the yaml header, tasks and the worklog's newest time of each item, never a proposal, note, event list or spec")]
    public void TheBoardReadsHeadersOnly()
    {
        string root = Shared.Value;
        OpenSpecStoreApi.Open(root);
        List<string> log = StartRecording();

        IReadOnlyList<WorkItems.PublicDTOs.WorkItem> items = WorkItemsApi.GetAll();

        Assert.Equal(Folders, items.Count);
        Assert.Equal((3, 1), (items[0].Subtasks.Count, items[0].Subtasks.Count(s => s.Done)));   // the board shows its subtask counts
        Assert.Equal(new DateTime(2026, 2, 2, 8, 0, 0), items[0].Modified);   // updated: the newest line of the worklog
        List<string> opened = Opened(log, root);
        Assert.Equal([".openspec.yaml", "events.log", "tasks.md"], opened.Select(What).Distinct().Order(StringComparer.Ordinal));
        Assert.DoesNotContain(opened, p => p.EndsWith("proposal.md") || p.Contains("/notes/") || p.Contains("/specs/"));
        Assert.Equal(Folders * 3, opened.Count);   // yaml, tasks and events.log, once each
    }

    [Fact(DisplayName = "Opening one item reads its proposal, notes and events, and nothing of the other items")]
    public void OpeningOneItemReadsOnlyItsOwnBodies()
    {
        string root = Shared.Value;
        OpenSpecStoreApi.Open(root);
        WorkItemsApi.GetAll();
        List<string> log = StartRecording();

        string description = ItemsCache.Description(42);
        IReadOnlyList<Notes.PublicDTOs.Note> notes = NotesCache.ForWorkItem(42);
        IReadOnlyList<WorkItems.PublicDTOs.WorkItemEvent> events = ItemsCache.Events(42);

        Assert.Contains("Why change 42 exists.", description);
        Assert.Equal([421, 422, 423], notes.Select(n => n.Id).Order());
        Assert.Single(events);
        List<string> opened = Opened(log, root);
        Assert.All(opened, p => Assert.StartsWith("change-042/", p));
        Assert.Equal(["change-042/events.log", "change-042/notes/n421.md", "change-042/notes/n422.md", "change-042/notes/n423.md", "change-042/proposal.md"], opened.Distinct().Order());
    }

    [Fact(DisplayName = "Building the board again with nothing changed parses no file: every stamp still matches")]
    public void AnUnchangedRepositoryParsesNothing()
    {
        string root = Shared.Value;
        OpenSpecStoreApi.Open(root);
        WorkItemsApi.GetAll();
        List<string> log = StartRecording();
        int folderReads = ItemsCache.FolderReads, loads = ItemsCache.Loads;

        OpenSpecStoreApi.Refresh();   // the version moves: the board is assembled again
        IReadOnlyList<WorkItems.PublicDTOs.WorkItem> items = WorkItemsApi.GetAll();

        Assert.Equal(loads + 1, ItemsCache.Loads);
        Assert.Equal(Folders, items.Count);
        Assert.Equal(folderReads, ItemsCache.FolderReads);
        Assert.Empty(Opened(log, root));
    }

    [Fact(DisplayName = "A changed file is read again, an unchanged one is not")]
    public void OnlyTheChangedFileIsReadAgain()
    {
        string root = Shared.Value;
        OpenSpecStoreApi.Open(root);
        Assert.Equal(1, WorkItemsApi.GetAll().First(w => w.Id == 7).Subtasks.Count(s => s.Done));
        List<string> log = StartRecording();
        string tasks = Path.Combine(Dir(root, 7), "tasks.md");
        Editing(tasks, () =>
        {
            File.WriteAllText(tasks, "## 1. Work\n\n- [x] 1.1 First\n- [x] 1.2 Second\n- [ ] 1.3 Third\n- [ ] 1.4 Fourth\n");   // another size
            File.SetLastWriteTimeUtc(tasks, Old.AddDays(1));
            int folderReads = ItemsCache.FolderReads;

            OpenSpecStoreApi.Refresh();
            WorkItems.PublicDTOs.WorkItem seven = WorkItemsApi.GetAll().First(w => w.Id == 7);

            Assert.Equal((4, 2), (seven.Subtasks.Count, seven.Subtasks.Count(s => s.Done)));
            Assert.Equal(folderReads + 1, ItemsCache.FolderReads);
            Assert.All(Opened(log, root), p => Assert.StartsWith("change-007/", p));
            Assert.Contains("change-007/tasks.md", Opened(log, root));
        });
    }

    [Fact(DisplayName = "The body cache never holds more than its bound and drops the least recently used item")]
    public void TheBodyCacheIsBounded()
    {
        string root = Shared.Value;
        OpenSpecStoreApi.Open(root);
        WorkItemsApi.GetAll();
        Assert.Equal(64, ItemsCache.BodyBound);
        List<string> log = StartRecording();

        for (int id = 1; id <= ItemsCache.BodyBound; id++) ItemsCache.Description(id);
        Assert.Equal(ItemsCache.BodyBound, ItemsCache.BodyCount);
        ItemsCache.Description(1);   // 1 is used again: now 2 is the least recently used
        ItemsCache.Description(ItemsCache.BodyBound + 1);
        ItemsCache.Description(ItemsCache.BodyBound + 2);

        Assert.Equal(ItemsCache.BodyBound, ItemsCache.BodyCount);
        Assert.Equal(ItemsCache.BodyBound + 2, Opened(log, root).Count(p => p.EndsWith("proposal.md")));   // 1 was served from memory
        log.Clear();
        ItemsCache.Description(1);   // still held
        Assert.Empty(Opened(log, root));
        ItemsCache.Description(2);   // dropped: read again
        Assert.Equal(["change-002/proposal.md"], Opened(log, root));
    }

    [Fact(DisplayName = "A file written in the same second as it was read is checked again on the next build")]
    public void ARacyFileIsCheckedAgain()
    {
        string root = Shared.Value;
        DateTime now = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
        Caches.S.Items.Clock = () => now;
        string tasks = Path.Combine(Dir(root, 9), "tasks.md");
        Editing(tasks, () =>
        {
            File.WriteAllText(tasks, "## 1. Work\n\n- [ ] 1.1 First\n- [ ] 1.2 Second\n");
            File.SetLastWriteTimeUtc(tasks, now);   // written at the time it is read
            OpenSpecStoreApi.Open(root);
            Assert.Equal(0, WorkItemsApi.GetAll().First(w => w.Id == 9).Subtasks.Count(s => s.Done));

            File.WriteAllText(tasks, "## 1. Work\n\n- [x] 1.1 First\n- [ ] 1.2 Second\n");   // the same size
            File.SetLastWriteTimeUtc(tasks, now);   // and the same time: size and time say nothing changed
            OpenSpecStoreApi.Refresh();
            Assert.Equal(1, WorkItemsApi.GetAll().First(w => w.Id == 9).Subtasks.Count(s => s.Done));   // the racy rule read it again

            now = now.AddMinutes(1);   // time has moved on past the file's: the next read settles it
            OpenSpecStoreApi.Refresh();
            WorkItemsApi.GetAll();
            List<string> log = StartRecording();
            OpenSpecStoreApi.Refresh();
            WorkItemsApi.GetAll();
            Assert.Empty(Opened(log, root));   // checked once, not on every build
        });
    }
}
