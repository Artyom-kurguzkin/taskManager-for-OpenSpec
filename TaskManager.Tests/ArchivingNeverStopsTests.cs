using System.Text.RegularExpressions;
using OpenSpecStore;
using WorkItems;

namespace TaskManager.Tests;

[Collection(nameof(ModuleStateCollection))]
public sealed class ArchivingNeverStopsTests
{
    private const string Reason = "Validation failed: spec.md has no requirements";

    /// <summary>Archives like the CLI: moves the change under changes/archive/, dated unless the folder already starts with a date.</summary>
    private static void Move(TestRepo repo, string folder)
    {
        string name = Regex.IsMatch(folder, @"^\d{4}-\d{2}-\d{2}-") ? folder : $"2026-11-01-{folder}";
        Directory.Move(repo.PathOf($"openspec/changes/{folder}"), repo.PathOf($"openspec/changes/archive/{name}"));
    }

    /// <summary>The CLI refuses until the try carries <paramref name="succeedWith"/>; every try's flags are recorded.</summary>
    private static Func<string, IReadOnlyList<string>, (bool, string, bool)> Archiver(TestRepo repo, List<string> tries, string? succeedWith) =>
        (folder, flags) =>
        {
            tries.Add(string.Join(' ', flags));
            if (succeedWith is null || !flags.Contains(succeedWith)) return (false, tries.Count == 1 ? Reason : "later: " + tries.Count, true);
            Move(repo, folder);
            return (true, "", true);
        };

    private static int HighestNote(TestRepo repo) =>
        Directory.EnumerateFiles(repo.PathOf("openspec/changes"), "n*.md", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(Path.GetDirectoryName(f)) == "notes").Select(f => int.Parse(Path.GetFileNameWithoutExtension(f)[1..])).DefaultIfEmpty(0).Max();

    private static string[] Notes(TestRepo repo, string archivedDir) =>
        Directory.Exists(repo.PathOf(archivedDir + "/notes")) ? Directory.GetFiles(repo.PathOf(archivedDir + "/notes")) : [];

    [Fact(DisplayName = "When the CLI refuses a completed item, the app archives it again without validation and shows nothing")]
    public void SecondTrySkipsValidation()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        List<string> tries = [];
        WorkItemsApi.ConfigureArchiverWithFlags(Archiver(repo, tries, "--no-validate"));
        try
        {
            Assert.Equal((true, ""), WorkItemsApi.SetStatus(107, "Completed"));
            Assert.Equal(["", "--no-validate"], tries);
            Assert.True(WorkItemsApi.Get(107)!.Archived);
            Assert.Empty(Notes(repo, "openspec/changes/archive/2026-11-01-export-remittances"));
        }
        finally { WorkItemsApi.ConfigureArchiver(null); }
    }

    [Fact(DisplayName = "When validation is not the problem, the item is archived without merging its specs and a note in the archive says why")]
    public void ThirdTryLeavesANote()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        int before = HighestNote(repo);
        List<string> tries = [];
        WorkItemsApi.ConfigureArchiverWithFlags(Archiver(repo, tries, "--skip-specs"));
        try
        {
            Assert.Equal((true, ""), WorkItemsApi.SetStatus(107, "Completed"));
            Assert.Equal(["", "--no-validate", "--no-validate --skip-specs"], tries);
            Assert.True(WorkItemsApi.Get(107)!.Archived);
            string file = Assert.Single(Notes(repo, "openspec/changes/archive/2026-11-01-export-remittances"));
            Assert.Equal($"n{before + 1}.md", Path.GetFileName(file));
            string text = File.ReadAllText(file).Replace("\r\n", "\n");
            Assert.StartsWith($"---\nid: {before + 1}\nby: App\ncreated: ", text);
            Assert.Contains("# Archived without merging its specs", text);
            Assert.Contains($"```\n{Reason}\n```", text);
        }
        finally { WorkItemsApi.ConfigureArchiver(null); }
    }

    [Fact(DisplayName = "An item whose folder already starts with a date gets the same note in the archive under that name")]
    public void DatedFolderGetsTheNote()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Directory.Move(repo.PathOf("openspec/changes/export-remittances"), repo.PathOf("openspec/changes/2026-10-01-export-remittances"));
        int before = HighestNote(repo);
        WorkItemsApi.ConfigureArchiverWithFlags(Archiver(repo, [], "--skip-specs"));
        try
        {
            Assert.Equal((true, ""), WorkItemsApi.SetStatus(107, "Completed"));
            string file = Assert.Single(Notes(repo, "openspec/changes/archive/2026-10-01-export-remittances"));
            Assert.Equal($"n{before + 1}.md", Path.GetFileName(file));
            Assert.Contains("# Archived without merging its specs", File.ReadAllText(file));
        }
        finally { WorkItemsApi.ConfigureArchiver(null); }
    }

    [Fact(DisplayName = "When every try is refused, the first message is shown and the item stays where it was")]
    public void AllTriesRefused()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        List<string> tries = [];
        WorkItemsApi.ConfigureArchiverWithFlags(Archiver(repo, tries, null));
        try
        {
            Assert.Equal((false, Reason), WorkItemsApi.SetStatus(107, "Completed"));
            Assert.Equal(3, tries.Count);
            Assert.Equal("Planned", WorkItemsApi.Get(107)!.Status);
        }
        finally { WorkItemsApi.ConfigureArchiver(null); }
    }

    [Fact(DisplayName = "When the OpenSpec CLI cannot run at all, the app asks once and shows the reason")]
    public void CouldNotRunIsNotRetried()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        int calls = 0;
        WorkItemsApi.ConfigureArchiverWithFlags((_, _) => { calls++; return (false, "Couldn't run the OpenSpec CLI", false); });
        try
        {
            Assert.Equal((false, "Couldn't run the OpenSpec CLI"), WorkItemsApi.SetStatus(107, "Completed"));
            Assert.Equal(1, calls);
        }
        finally { WorkItemsApi.ConfigureArchiver(null); }
    }

    [Fact(DisplayName = "A spec for a feature the main specs don't have yet is still tidied before archiving")]
    public void MissingMainSpecIsAnEmptyOne()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        const string delta = "openspec/changes/export-remittances/specs/functional/brand-new/spec.md";
        repo.Write(delta, "## ADDED Requirements\n\n### Requirement: Export\nThe system SHALL export.\n\n#### Scenario: Works\n- **WHEN** exported\n- **THEN** a file appears\n\n## REMOVED Requirements\n\n### Requirement: Fax\n**Reason**: gone\n");
        string? seen = null;
        WorkItemsApi.ConfigureArchiver(folder => { seen = repo.Read(delta); Move(repo, folder); return (true, ""); });
        try
        {
            Assert.True(WorkItemsApi.SetStatus(107, "Completed").Success);
            Assert.NotNull(seen);
            Assert.DoesNotContain("Fax", seen);   // a removal of something the (empty) main spec lacks is dropped
            Assert.Contains("### Requirement: Export", seen);
        }
        finally { WorkItemsApi.ConfigureArchiver(null); }
    }
}
