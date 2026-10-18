using OpenSpecStore;

namespace TaskManager.Tests;

[Collection(nameof(ModuleStateCollection))]
public sealed class EditingRepoFilesSafelyTests
{
    private const string Yaml = "openspec/changes/add-health-check/.openspec.yaml";

    [Fact(DisplayName = "The app reads only its own x-rtm block from .openspec.yaml")]
    public void ReadsRtmBlock()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        string? block = OpenSpecStoreApi.ReadRtmBlock(repo.PathOf(Yaml));

        Assert.NotNull(block);
        Assert.StartsWith("id: 123", block);
        Assert.Contains("status: In Progress", block);
        Assert.DoesNotContain("schema:", block);
    }

    [Fact(DisplayName = "A change created outside the app has no x-rtm block")]
    public void NoRtmBlock()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Assert.Null(OpenSpecStoreApi.ReadRtmBlock(repo.PathOf("openspec/changes/plain-change/.openspec.yaml")));
    }

    [Fact(DisplayName = "Saving metadata keeps every other key and comment byte-for-byte")]
    public void WriteKeepsOtherLines()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string before = repo.Read(Yaml);
        string path = repo.PathOf(Yaml);
        OpenSpecStoreApi.ReadRtmBlock(path);

        (bool ok, string _) = OpenSpecStoreApi.WriteRtmBlock(path, "id: 123\nstatus: Blocked\n");

        Assert.True(ok);
        string after = repo.Read(Yaml);
        string head = before[..before.IndexOf("x-rtm:", StringComparison.Ordinal)];
        Assert.StartsWith(head, after);                         // schema, created and the comment untouched
        Assert.EndsWith("x-rtm:\n  id: 123\n  status: Blocked\n", after);
    }

    [Fact(DisplayName = "Saving metadata to a change without x-rtm appends the block")]
    public void WriteAppendsBlock()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string rel = "openspec/changes/plain-change/.openspec.yaml";
        string before = repo.Read(rel);
        OpenSpecStoreApi.ReadRtmBlock(repo.PathOf(rel));

        OpenSpecStoreApi.WriteRtmBlock(repo.PathOf(rel), "id: 125\n");

        Assert.Equal(before + "x-rtm:\n  id: 125\n", repo.Read(rel));
    }

    [Fact(DisplayName = "Saving one section of the x-rtm block keeps the block's other sections")]
    public void SectionWriteKeepsSiblings()
    {
        using TestRepo repo = new();
        repo.Write("openspec/config.yaml", "schema: spec-driven\nx-rtm:\n  lanes:\n    In Progress: [High, Low]\n  features:\n  - functional/exports\n");
        OpenSpecStoreApi.Open(repo.Root);
        string path = repo.PathOf("openspec/config.yaml");

        Assert.Equal("In Progress: [High, Low]\n", OpenSpecStoreApi.ReadRtmSection(path, "lanes"));
        OpenSpecStoreApi.WriteRtmSection(path, "lanes", "Blocked:\n- Needs review\n");

        Assert.Equal("schema: spec-driven\nx-rtm:\n  lanes:\n    Blocked:\n    - Needs review\n  features:\n  - functional/exports\n", repo.Read("openspec/config.yaml"));
        OpenSpecStoreApi.WriteRtmSection(path, "lanes", null);
        Assert.Equal("schema: spec-driven\nx-rtm:\n  features:\n  - functional/exports\n", repo.Read("openspec/config.yaml"));
    }

    [Fact(DisplayName = "An edit is refused when the file changed on disk after the app read it")]
    public void StaleWriteRefused()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string path = repo.PathOf("openspec/changes/add-health-check/tasks.md");
        OpenSpecStoreApi.ReadText(path);
        repo.Write("openspec/changes/add-health-check/tasks.md", "# Tasks\n\nedited elsewhere\n");

        (bool ok, string message) = OpenSpecStoreApi.WriteText(path, "# Tasks\n\nmine\n");

        Assert.False(ok);
        Assert.Equal("This file changed on disk; your edit was not saved", message);
        Assert.Contains("edited elsewhere", repo.Read("openspec/changes/add-health-check/tasks.md"));
    }

    [Fact(DisplayName = "Writing a new file creates its folder")]
    public void WriteNewFile()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        (bool ok, string _) = OpenSpecStoreApi.WriteText(repo.PathOf("openspec/changes/new-one/proposal.md"), "# Proposal\n");
        Assert.True(ok);
        Assert.Equal("# Proposal\n", repo.Read("openspec/changes/new-one/proposal.md"));
    }
}
