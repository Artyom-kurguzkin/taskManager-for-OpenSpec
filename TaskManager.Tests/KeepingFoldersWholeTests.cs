using System.Text;
using System.Text.Json;
using BoardSync;
using BoardSync.PublicDTOs;
using Notes;
using OpenSpecStore;

namespace TaskManager.Tests;

/// <summary>
/// A change folder is one unit: while a file in it is still being written the sync leaves the whole folder for the next pass, and a folder that arrived in the storage only in part
/// (the computer that made it closed in the middle) is not taken by the other computers until the one that made it has uploaded the rest. Two or three computers (directories with
/// their own state folders) and one folder storage; the clock is injected, nothing waits.
/// </summary>
public sealed class KeepingFoldersWholeTests : IDisposable
{
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("rtm-whole-");
    private DateTime _now = new(2026, 11, 27, 8, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan TenSeconds = TimeSpan.FromSeconds(10);

    public void Dispose() { try { _temp.Delete(true); } catch (IOException) { } }

    private sealed record Computer(string Id, string Root, string State, IBoardStorage Storage);

    private Computer NewComputer(string id) =>
        new(id, Path.Combine(_temp.FullName, id, "changes"), Path.Combine(_temp.FullName, id, "state"), BoardSyncApi.FolderStorage(Path.Combine(_temp.FullName, "storage")));

    private Task<SyncReport> Sync(Computer c) => BoardSyncApi.Sync(new SyncOptions(c.Root, c.State, c.Storage, c.Id, () => _now, SettleWindow: TenSeconds));

    private void Tick(int minutes = 5) => _now = _now.AddMinutes(minutes);

    /// <summary>Writes a file whose last write was <paramref name="age"/> before the injected clock.</summary>
    private void Write(Computer c, string path, string text, TimeSpan? age = null)
    {
        string file = Path.Combine(c.Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text, new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(file, _now - (age ?? TimeSpan.FromHours(1)));
    }

    private static string Read(Computer c, string path) => File.ReadAllText(Path.Combine(c.Root, path));
    private static bool FolderExists(Computer c, string path) => Directory.Exists(Path.Combine(c.Root, path));

    private static SortedDictionary<string, string> Tree(Computer c) =>
        new(Directory.Exists(c.Root)
            ? Directory.EnumerateFiles(c.Root, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(c.Root, f).Replace('\\', '/'), File.ReadAllText)
            : []);

    private static HashSet<string> LiveInStorage(Computer c) =>
        [.. JsonDocument.Parse(c.Storage.Get("manifest.json").GetAwaiter().GetResult()!.Bytes).RootElement.GetProperty("entries").EnumerateObject()
            .Where(p => !p.Value.GetProperty("deleted").GetBoolean()).Select(p => p.Name)];

    private static string Yaml(int id, string title, string extra = "") =>
        $"schema: spec-driven\ncreated: 2026-11-01\nx-rtm:\n  id: {id}\n  title: {title}\n  status: Planned\n  priority: low\n  contributors: [Ann]\n  commits: [aaa1111]\n{extra}";

    private static string NoteText(int id, string title, string extra = "") => $"---\nid: {id}\nby: Ann\ncreated: 2026-11-10T10:00:00\n{extra}---\n# {title}\n\nWhat happened.\n";

    private void Seed(Computer c, string folder, int id, string title, TimeSpan? age = null)
    {
        Write(c, $"{folder}/.openspec.yaml", Yaml(id, title), age);
        Write(c, $"{folder}/proposal.md", $"# {title}\n\n## Why\n\nBecause.\n", age);
        Write(c, $"{folder}/tasks.md", "# Tasks\n\n## 1. Tasks\n\n- [ ] 1.1 first\n- [ ] 1.2 second\n", age);
        Write(c, $"{folder}/events.log", "2026-11-10T10:00:00+00:00 | Ann | created | seeded\n", age);
        Write(c, $"{folder}/notes/n{id}1.md", NoteText(id * 10 + 1, $"Note of {title}"), age);
    }

    private const string Merged = "merge-wi12-wi13";

    /// <summary>The files of the merged item as the app's merge writes them, each at its own age (a name given in <paramref name="fresh"/> was written just now).</summary>
    private void WriteMerged(Computer c, string[]? only = null, params string[] fresh)
    {
        TimeSpan? Age(string path) => fresh.Contains(path) ? TimeSpan.FromSeconds(2) : null;
        Dictionary<string, string> files = new()
        {
            [".openspec.yaml"] = Yaml(40, "Add health check", "  mergedFrom: [12, 13]\n  mergedFolders: [add-health-check, add-ping]\n"),
            ["proposal.md"] = "# Add health check\n\n## Why\n\nBecause.\n",
            ["tasks.md"] = "# Tasks\n\n## 1. Add health check\n\n- [ ] 1.1 first\n\n## 2. #wi13 Add ping\n\n- [ ] 2.1 first\n",
            ["events.log"] = "2026-11-10T10:00:00+00:00 | Ann | created | seeded\n2026-11-27T07:00:00+00:00 | Ann | merged-from | #wi13 Add ping\n",
            ["notes/n121.md"] = NoteText(121, "Note of Add health check"),
            ["notes/n131.md"] = NoteText(131, "Note of Add ping", "mergedFrom: 13\nmergedAt: 2026-11-27\n"),
        };
        foreach ((string rest, string text) in files)
            if (only is null || only.Contains(rest)) Write(c, $"{Merged}/{rest}", text, Age(rest));
    }

    private async Task<(Computer A, Computer B)> TwoComputersWithTheSources()
    {
        Computer a = NewComputer("A"), b = NewComputer("B");
        Seed(a, "add-health-check", 12, "Add health check");
        Seed(a, "add-ping", 13, "Add ping");
        await Sync(a);
        Tick();
        await Sync(b);
        Tick();
        return (a, b);
    }

    // ---- the tests -------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "A folder with files still settling is not uploaded in part")]
    public async Task AFolderWithFilesStillSettlingIsNotUploadedInPart()
    {
        Computer a = NewComputer("A"), b = NewComputer("B");
        Seed(a, "quiet-item", 20, "Quiet item");
        WriteMerged(a, null, "events.log", "tasks.md", "notes/n131.md");      // three of the six files were written two seconds ago, the rest an hour ago

        SyncReport first = await Sync(a);

        Assert.Equal(["quiet-item/.openspec.yaml", "quiet-item/events.log", "quiet-item/notes/n201.md", "quiet-item/proposal.md", "quiet-item/tasks.md"], first.Uploaded.Order(StringComparer.Ordinal));
        Assert.Equal([Merged], first.WaitingFolders);
        Assert.Equal([$"{Merged}/events.log", $"{Merged}/notes/n131.md", $"{Merged}/tasks.md"], first.Settling.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(LiveInStorage(a), p => p.StartsWith(Merged + "/", StringComparison.Ordinal));      // not one of the settled files went up either

        _now = _now.AddSeconds(30);
        SyncReport second = await Sync(a);

        Assert.Empty(second.WaitingFolders);
        Assert.Empty(second.Settling);
        Assert.Equal(6, second.Uploaded.Count(p => p.StartsWith(Merged + "/", StringComparison.Ordinal)));
        Tick();
        await Sync(b);
        Assert.Equal(Tree(a), Tree(b));
    }

    [Fact(DisplayName = "An incomplete folder in the storage is held back")]
    public async Task AnIncompleteFolderInTheStorageIsHeldBack()
    {
        (Computer a, Computer b) = await TwoComputersWithTheSources();
        SortedDictionary<string, string> before = Tree(b);

        // Another computer left only the merged item's notes in the storage: no .openspec.yaml.
        Computer p = NewComputer("P");
        WriteMerged(p, ["notes/n121.md", "notes/n131.md"]);
        await Sync(p);
        Tick();
        SyncReport onB = await Sync(b);

        Assert.Equal([Merged], onB.IncompleteFolders);
        Assert.DoesNotContain(onB.Downloaded, d => d.StartsWith(Merged + "/", StringComparison.Ordinal));
        Assert.False(FolderExists(b, Merged));
        Assert.Equal(before, Tree(b));                                                // nothing of it reached disk, and the sources are as they were

        // The yaml has arrived, but the sources it names are still live in the storage: the folder is still not whole.
        WriteMerged(p);
        Tick();
        await Sync(p);
        Tick();
        SyncReport again = await Sync(b);

        Assert.Equal([Merged], again.IncompleteFolders);
        Assert.False(FolderExists(b, Merged));
        Assert.Equal(before, Tree(b));
        Assert.Equal(SyncOutcome.NothingToDo, again.Outcome);                        // not counted as work done
        Assert.True(FolderExists(a, "add-ping"));
    }

    [Fact(DisplayName = "Sources of an incomplete merged folder keep their note ids")]
    public void SourcesOfAnIncompleteMergedFolderKeepTheirNoteIds()
    {
        using TestRepo repo = new();
        // #wi107 (export-remittances) is the source; the merged item copied its note n500 (stamped mergedFrom: 107) and the copy was made first, so by age the source's note is the later twin.
        repo.Write("openspec/changes/export-remittances/notes/n500.md", "---\nid: 500\nby: Tom\ncreated: 2026-10-20T10:00:00\n---\n# Remittance note\n\nOriginal.\n");
        repo.Write("openspec/changes/merged-item/.openspec.yaml", "schema: spec-driven\ncreated: 2026-10-10\nx-rtm:\n  id: 400\n  title: Merged item\n  status: Planned\n  priority: low\n  contributors: [Tom]\n  mergedFrom: [107]\n  mergedFolders: [export-remittances]\n");
        repo.Write("openspec/changes/merged-item/proposal.md", "# Merged item\n");
        repo.Write("openspec/changes/merged-item/notes/n500.md", "---\nid: 500\nby: Tom\ncreated: 2026-10-10T10:00:00\nmergedFrom: 107\nmergedAt: 2026-10-11\n---\n# Remittance note\n\nOriginal.\n");
        OpenSpecStoreApi.Open(repo.Root);

        IReadOnlyList<(int OldId, int NewId, string Path)> renumbered = NotesApi.RepairDuplicateIds();

        Assert.Empty(renumbered);                                                      // the sources are still here, so the merge is not whole: nothing is renumbered
        Assert.Contains("id: 500", repo.Read("openspec/changes/export-remittances/notes/n500.md"));
        Assert.False(File.Exists(repo.PathOf("openspec/changes/export-remittances/notes/n501.md")));
    }

    [Fact(DisplayName = "The computer that made the merge completes a half-uploaded folder on its next sync")]
    public async Task TheComputerThatMadeTheMergeCompletesAHalfUploadedFolder()
    {
        (Computer a, Computer b) = await TwoComputersWithTheSources();
        SortedDictionary<string, string> before = Tree(b);

        // The merge on A, closed in the middle: the notes made it into the storage, the rest of the folder did not (and the sources went back, since nothing says where they went).
        WriteMerged(a, ["notes/n121.md", "notes/n131.md"]);
        Directory.Delete(Path.Combine(a.Root, "add-health-check"), true);
        Directory.Delete(Path.Combine(a.Root, "add-ping"), true);
        Tick();
        await Sync(a);
        Tick();
        SyncReport onB = await Sync(b);

        Assert.Equal([Merged], onB.IncompleteFolders);
        Assert.Equal(before, Tree(b));                                                // B keeps its sources whole, with their own note ids

        // A is opened again: the rest of the folder is written, the sources are gone, and the next sync uploads what is missing.
        WriteMerged(a);
        foreach (string source in new[] { "add-health-check", "add-ping" }) if (FolderExists(a, source)) Directory.Delete(Path.Combine(a.Root, source), true);
        Tick();
        SyncReport onA = await Sync(a);

        Assert.Contains($"{Merged}/.openspec.yaml", onA.Uploaded);
        Assert.Contains($"{Merged}/events.log", onA.Uploaded);
        Assert.Empty(onA.IncompleteFolders);
        Assert.Contains($"{Merged}/.openspec.yaml", LiveInStorage(a));
        Assert.DoesNotContain(LiveInStorage(a), p => p.StartsWith("add-ping/", StringComparison.Ordinal));      // the sources are removed in the storage once the folder is whole

        Tick();
        SyncReport finished = await Sync(b);

        Assert.Empty(finished.IncompleteFolders);
        Assert.False(FolderExists(b, "add-ping"));
        Assert.Contains("mergedFrom: [12, 13]", Read(b, $"{Merged}/.openspec.yaml"));
        Assert.Equal(Tree(a), Tree(b));
    }
}
