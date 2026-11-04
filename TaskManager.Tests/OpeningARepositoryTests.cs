using OpenSpecStore;
using WorkItems;

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

    [Fact(DisplayName = "Two repositories can be open at once: Use switches between them, each keeps its own parsed data, and a plain Open closes the others")]
    public void TwoRepositoriesAtOnce()
    {
        using TestRepo a = new();
        using TestRepo b = new();
        b.Write("openspec/changes/export-remittances/.openspec.yaml", "schema: spec-driven\nx-rtm:\n  id: 107\n  title: Second repo item\n");
        Assert.True(OpenSpecStoreApi.Open(a.Root).Success);
        Assert.True(OpenSpecStoreApi.Open(b.Root, keepOthers: true).Success);
        Assert.Equal([a.Root, b.Root], OpenSpecStoreApi.OpenRoots);
        Assert.Equal("Second repo item", WorkItemsApi.Get(107)!.Title);

        Assert.True(OpenSpecStoreApi.Use(a.Root));
        Assert.Equal("Monthly remittance export", WorkItemsApi.Get(107)!.Title);
        int loads = WorkItemsModule.Loads;
        OpenSpecStoreApi.Use(b.Root); WorkItemsApi.Get(107);
        OpenSpecStoreApi.Use(a.Root); WorkItemsApi.Get(107);
        Assert.Equal(loads, WorkItemsModule.Loads);   // switching costs no parse: each repository keeps its own cache
        Assert.False(OpenSpecStoreApi.Use(Path.GetTempPath()));   // not open

        Assert.True(OpenSpecStoreApi.Open(a.Root).Success);
        Assert.Equal([a.Root], OpenSpecStoreApi.OpenRoots);
    }

    [Fact(DisplayName = "Opening a folder without openspec/ sets it up (config.yaml, specs/, changes/archive/) and says so; a folder that does not exist is refused")]
    public void FolderWithoutOpenSpec()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string empty = Directory.CreateTempSubdirectory("rtm-empty").FullName;
        try
        {
            (bool ok, string message) = OpenSpecStoreApi.Open(empty);

            Assert.True(ok, message);
            Assert.Equal(empty, OpenSpecStoreApi.RepoRoot);
            Assert.Contains("Set up openspec/", message);
            Assert.StartsWith("schema: spec-driven", File.ReadAllText(Path.Combine(empty, "openspec", "config.yaml")));
            Assert.True(Directory.Exists(Path.Combine(empty, "openspec", "specs")));
            Assert.True(Directory.Exists(Path.Combine(empty, "openspec", "changes", "archive")));
            Assert.Empty(WorkItemsApi.GetAll(includeHidden: true));
            Assert.Equal((true, ""), OpenSpecStoreApi.Open(empty));   // the second time there is nothing to set up

            (ok, message) = OpenSpecStoreApi.Open(Path.Combine(empty, "nope"));
            Assert.False(ok);
            Assert.Contains("nope", message);
            Assert.Equal(empty, OpenSpecStoreApi.RepoRoot);
        }
        finally { OpenSpecStoreApi.Open(repo.Root); Directory.Delete(empty, true); }
    }
}
