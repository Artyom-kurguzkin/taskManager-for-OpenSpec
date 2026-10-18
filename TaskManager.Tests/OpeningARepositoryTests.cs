using OpenSpecStore;

namespace TaskManager.Tests;

[Collection(nameof(ModuleStateCollection))]
public sealed class OpeningARepositoryTests
{
    [Fact(DisplayName = "Opening a folder that contains openspec/ makes it the workspace")]
    public void FolderWithOpenSpec()
    {
        using TestRepo repo = new();
        (bool ok, string _) = OpenSpecStoreApi.Open(repo.Root);
        Assert.True(ok);
        Assert.Equal(repo.Root, OpenSpecStoreApi.RepoRoot);
    }

    [Fact(DisplayName = "Opening a folder without openspec/ explains how to fix it and keeps the previous workspace")]
    public void FolderWithoutOpenSpec()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string empty = Directory.CreateTempSubdirectory("rtm-empty").FullName;

        (bool ok, string message) = OpenSpecStoreApi.Open(empty);

        Assert.False(ok);
        Assert.Equal("No openspec/ folder here. Run `openspec init` in this repo first.", message);
        Assert.Equal(repo.Root, OpenSpecStoreApi.RepoRoot);
    }
}
