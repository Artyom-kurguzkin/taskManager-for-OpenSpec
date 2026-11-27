using BoardSync.Sync;

namespace TaskManager.Tests;

/// <summary>The folder pass: a change folder that one computer lacks is looked up by id (archive, merge, rename) before anything is read as a deletion.</summary>
public sealed class FollowingMovedFoldersTests
{
    private static ChangeFolder Folder(string path, int? id, int[]? mergedFrom = null, string[]? mergedFolders = null, int[]? mergedLines = null) =>
        new(path, id, mergedFrom ?? [], mergedFolders, mergedLines ?? []);

    private static FolderView View(params ChangeFolder[] folders) => new(folders);

    private static FolderResolution Only(FolderPassResult result, FolderSide lacks)
    {
        FolderResolution single = Assert.Single(result.Resolutions, r => r.Lacks == lacks);
        return single;
    }

    [Fact(DisplayName = "A folder archived elsewhere is found by its id")]
    public void Archived()
    {
        FolderView baseView = View(Folder("add-health-check", 12));
        FolderView local = View(Folder("add-health-check", 12));
        FolderView remote = View(Folder("archive/2026-11-20-add-health-check", 12));

        FolderPassResult result = FolderPass.Resolve(local, remote, baseView);

        FolderResolution r = Assert.Single(result.Resolutions);
        Assert.Equal("add-health-check", r.Folder);
        Assert.Equal(12, r.Id);
        Assert.Equal(FolderSide.Remote, r.Lacks);
        Assert.Equal(FolderFate.Archived, r.Fate);
        Assert.Equal("archive/2026-11-20-add-health-check", r.Destination);
        Assert.Empty(result.Clashes);
    }

    [Fact(DisplayName = "A folder merged elsewhere is found through mergedFrom")]
    public void Merged()
    {
        FolderView baseView = View(Folder("add-health-check", 12), Folder("add-ping", 13));
        FolderView local = View(Folder("add-health-check", 12), Folder("add-ping", 13));
        FolderView remote = View(Folder("merge-wi12-wi13", 40, mergedFrom: [12, 13], mergedFolders: ["add-health-check", "add-ping"]));

        FolderPassResult result = FolderPass.Resolve(local, remote, baseView);

        Assert.Equal(2, result.Resolutions.Count);
        Assert.All(result.Resolutions, r =>
        {
            Assert.Equal(FolderSide.Remote, r.Lacks);
            Assert.Equal(FolderFate.Merged, r.Fate);
            Assert.Equal("merge-wi12-wi13", r.Destination);
        });
        Assert.Equal(["add-health-check", "add-ping"], result.Resolutions.Select(r => r.Folder).Order(StringComparer.Ordinal));
    }

    [Fact(DisplayName = "An old merge is found through its merged-from line")]
    public void OldMerge()
    {
        FolderView baseView = View(Folder("add-health-check", 12));
        FolderView local = View(Folder("add-health-check", 12));
        // A merge made before mergedFrom existed: only the event log says "merged-from #wi12 ...".
        FolderView remote = View(Folder("merge-wi12-wi13", 40, mergedLines: [12, 13]));

        FolderPassResult result = FolderPass.Resolve(local, remote, baseView);

        FolderResolution r = Assert.Single(result.Resolutions);
        Assert.Equal(FolderFate.Merged, r.Fate);
        Assert.Equal("merge-wi12-wi13", r.Destination);
    }

    [Fact(DisplayName = "A renamed folder is found by its id")]
    public void Renamed()
    {
        FolderView baseView = View(Folder("add-health-check", 12));
        FolderView local = View(Folder("health-check", 12));
        FolderView remote = View(Folder("add-health-check", 12));

        FolderPassResult result = FolderPass.Resolve(local, remote, baseView);

        FolderResolution r = Assert.Single(result.Resolutions);
        Assert.Equal(FolderSide.Local, r.Lacks);
        Assert.Equal(FolderFate.Renamed, r.Fate);
        Assert.Equal("health-check", r.Destination);
    }

    [Fact(DisplayName = "A folder gone with no explanation is marked to be put back")]
    public void Missing()
    {
        FolderView baseView = View(Folder("add-health-check", 12), Folder("other", 14));
        // A merge that names another item, a folder with another id: neither explains id 12.
        FolderView local = View(Folder("add-health-check", 12), Folder("other", 14));
        FolderView remote = View(Folder("other", 14), Folder("merge-wi30-wi31", 41, mergedFrom: [30, 31], mergedFolders: ["a", "b"]),
            Folder("archive/2026-11-01-unrelated", 99));

        FolderPassResult result = FolderPass.Resolve(local, remote, baseView);

        FolderResolution r = Assert.Single(result.Resolutions);
        Assert.Equal("add-health-check", r.Folder);
        Assert.Equal(FolderSide.Remote, r.Lacks);
        Assert.Equal(FolderFate.Missing, r.Fate);
        Assert.Null(r.Destination);
    }

    [Fact(DisplayName = "Both sides archiving on different days keeps the earlier folder")]
    public void BothArchived()
    {
        FolderView baseView = View(Folder("add-health-check", 12));
        FolderView local = View(Folder("archive/2026-11-22-add-health-check", 12));
        FolderView remote = View(Folder("archive/2026-11-20-add-health-check", 12));

        FolderPassResult result = FolderPass.Resolve(local, remote, baseView);

        Assert.Equal(FolderFate.Archived, Only(result, FolderSide.Local).Fate);
        Assert.Equal(FolderFate.Archived, Only(result, FolderSide.Remote).Fate);
        FolderClash clash = Assert.Single(result.Clashes);
        Assert.Equal(FolderClashKind.EarlierArchiveKept, clash.Kind);
        Assert.Equal(12, clash.Id);
        Assert.Equal("archive/2026-11-20-add-health-check", clash.Keep);
        Assert.Equal("archive/2026-11-22-add-health-check", clash.MergeAway);

        // One archived, one merged: both stay, each is noted.
        FolderView mergedRemote = View(Folder("merge-wi12-wi13", 40, mergedFrom: [12, 13], mergedFolders: ["add-health-check", "add-ping"]));
        FolderPassResult second = FolderPass.Resolve(local, mergedRemote, baseView);
        FolderClash noted = Assert.Single(second.Clashes);
        Assert.Equal(FolderClashKind.NotedOnEach, noted.Kind);
        Assert.Equal("archive/2026-11-22-add-health-check", noted.Local);
        Assert.Equal("merge-wi12-wi13", noted.Remote);
    }
}
