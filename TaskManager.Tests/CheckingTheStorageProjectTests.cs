using System.Text;
using System.Text.Json;
using BoardSync;
using BoardSync.PublicDTOs;

namespace TaskManager.Tests;

/// <summary>
/// The storage's list names the project it belongs to (the hash of the repository's first commit): a sync of another project stops before it changes anything,
/// and a list without the name is given one. With no saved base for a file, the version in the last commit is the base, so edits made on top of it are merged, not overwritten.
/// </summary>
public sealed class CheckingTheStorageProjectTests : IDisposable
{
    private const string Dir = "add-health-check";
    private const string Tasks = Dir + "/tasks.md";
    private const string Proposal = Dir + "/proposal.md";
    private const string SeedTasks = "# Tasks\n\n## 1. Tasks\n\n- [ ] 1.1 write the test\n- [ ] 1.2 write the code\n";
    private const string SeedProposal = "# Add health check\n\n## Why\n\nWe cannot tell whether the service is up.\n";
    private const string ProjectA = "1111111111111111111111111111111111111111";
    private const string ProjectB = "2222222222222222222222222222222222222222";
    private const string Other = "This storage holds the board of another project. Nothing was changed. Check the bucket and prefix in Settings.";

    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("rtm-sync-project-");
    private DateTime _now = new(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);

    public void Dispose() { try { _temp.Delete(true); } catch (IOException) { } }

    private string StorageFolder => Path.Combine(_temp.FullName, "storage");
    private string Root(string id) => Path.Combine(_temp.FullName, id, "changes");

    private Task<SyncReport> Sync(string id, string? project, Func<string, string?>? committed = null) =>
        BoardSyncApi.Sync(new SyncOptions(Root(id), Path.Combine(_temp.FullName, id, "state"), BoardSyncApi.FolderStorage(StorageFolder), id, () => _now, ProjectId: project, CommittedContent: committed));

    private void Write(string id, string path, string text)
    {
        string file = Path.Combine(Root(id), path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text, new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(file, _now);
    }

    private string Read(string id, string path) => File.ReadAllText(Path.Combine(Root(id), path));

    private string? Board() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(StorageFolder, "manifest.json"))).RootElement.TryGetProperty("board", out JsonElement b) ? b.GetString() : null;

    private List<string> Everything(string folder) =>
        [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).Select(f => Path.GetRelativePath(folder, f) + "|" + Convert.ToHexString(File.ReadAllBytes(f)))];

    [Fact(DisplayName = "A storage that holds another project's board changes nothing")]
    public async Task AStorageThatHoldsAnotherProjectsBoardChangesNothing()
    {
        Write("A", Tasks, SeedTasks);
        await Sync("A", ProjectA);
        Assert.Equal(ProjectA, Board());
        List<string> storageBefore = Everything(StorageFolder);

        Write("C", Proposal, SeedProposal);      // a computer of another project, with a board of its own
        List<string> localBefore = Everything(Root("C"));
        _now = _now.AddMinutes(5);
        SyncReport stopped = await Sync("C", ProjectB);

        Assert.Equal(SyncOutcome.Blocked, stopped.Outcome);
        Assert.Equal(SyncBlock.OtherProject, stopped.Blocked);
        Assert.Equal(Other, stopped.Message);
        Assert.Equal(storageBefore, Everything(StorageFolder));
        Assert.Equal(localBefore, Everything(Root("C")));
        Assert.False(File.Exists(Path.Combine(Root("C"), Tasks)));

        SyncReport same = await Sync("B", ProjectA);                 // the same project on another computer is let in
        Assert.Equal(SyncOutcome.Synced, same.Outcome);
        Assert.Equal(SeedTasks, Read("B", Tasks));
    }

    [Fact(DisplayName = "A list without a project is stamped")]
    public async Task AListWithoutAProjectIsStamped()
    {
        Write("A", Tasks, SeedTasks);
        await Sync("A", null);
        Assert.Null(Board());

        _now = _now.AddMinutes(5);
        SyncReport stamped = await Sync("A", ProjectA);               // nothing else changed: the list still gets its project
        Assert.NotEqual(SyncOutcome.Blocked, stamped.Outcome);
        Assert.Equal(ProjectA, Board());
        byte[] manifest = File.ReadAllBytes(Path.Combine(StorageFolder, "manifest.json"));

        _now = _now.AddMinutes(5);
        await Sync("A", ProjectA);
        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(StorageFolder, "manifest.json")));    // stamped once, then left alone
        _now = _now.AddMinutes(5);
        await Sync("A", null);                                         // a project without git keeps what is there
        Assert.Equal(ProjectA, Board());
    }

    [Fact(DisplayName = "With no saved base the committed version is the base")]
    public async Task WithNoSavedBaseTheCommittedVersionIsTheBase()
    {
        Write("A", Tasks, SeedTasks);
        await Sync("A", ProjectA);
        _now = _now.AddMinutes(5);
        Write("A", Tasks, SeedTasks.Replace("- [ ] 1.1", "- [x] 1.1"));
        await Sync("A", ProjectA);

        // B has never synced: its tasks file is the committed one with one line ticked on top.
        _now = _now.AddMinutes(5);
        Write("B", Tasks, SeedTasks.Replace("- [ ] 1.2", "- [x] 1.2"));
        SyncReport report = await Sync("B", ProjectA, path => path == Tasks ? SeedTasks : null);

        Assert.Equal(SyncOutcome.Synced, report.Outcome);
        string merged = Read("B", Tasks);
        Assert.Contains("- [x] 1.1", merged);
        Assert.Contains("- [x] 1.2", merged);
    }
}
